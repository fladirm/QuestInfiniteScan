"""Single-GPU durable scheduler; networking never enters the compute path."""

from __future__ import annotations

import asyncio
import logging

from .backend import BackendContext, BackendJobError, ComputeBackend, JobCanceledError
from .storage import JobStateError, JobStore


LOGGER = logging.getLogger(__name__)


class JobScheduler:
    def __init__(self, store: JobStore, backend: ComputeBackend) -> None:
        self.store = store
        self.backend = backend
        self._wake = asyncio.Event()
        self._stopping = False
        self._task: asyncio.Task[None] | None = None

    async def start(self) -> int:
        if self._task is not None:
            return 0
        recovered = self.store.recover_interrupted()
        self._stopping = False
        self._task = asyncio.create_task(self._run_loop(), name="qis-job-scheduler")
        self._wake.set()
        return recovered

    async def stop(self) -> None:
        self._stopping = True
        self._wake.set()
        task = self._task
        self._task = None
        if task is not None:
            await task

    def notify(self) -> None:
        self._wake.set()

    async def _run_loop(self) -> None:
        while not self._stopping:
            claimed = self.store.claim_next()
            if claimed is None:
                self._wake.clear()
                try:
                    await asyncio.wait_for(self._wake.wait(), timeout=1.0)
                except TimeoutError:
                    pass
                continue
            submission, upload = claimed
            context = BackendContext(submission, upload, self.store)
            try:
                result = await self.backend.run(context)
                if self.store.is_cancel_requested(submission.job_id):
                    result.artifact_path.unlink(missing_ok=True)
                    self.store.acknowledge_running_cancel(submission.job_id)
                else:
                    self.store.complete(
                        submission.job_id, result.artifact_path, result.descriptor
                    )
            except JobCanceledError:
                self.store.acknowledge_running_cancel(submission.job_id)
            except BackendJobError as exception:
                try:
                    self.store.fail(
                        submission.job_id,
                        exception.code,
                        exception.message,
                    )
                except JobStateError:
                    pass
            except asyncio.CancelledError:
                raise
            except Exception as exception:  # worker failures become durable status
                LOGGER.exception("backend failed for %s", submission.job_id)
                try:
                    self.store.fail(
                        submission.job_id,
                        "backend_failure",
                        f"{type(exception).__name__}: {exception}",
                    )
                except JobStateError:
                    pass
