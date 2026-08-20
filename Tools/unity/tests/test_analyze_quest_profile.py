from __future__ import annotations

import importlib.util
import json
from pathlib import Path
import tempfile
import unittest


MODULE_PATH = Path(__file__).resolve().parents[1] / "analyze_quest_profile.py"
SPEC = importlib.util.spec_from_file_location("analyze_quest_profile", MODULE_PATH)
assert SPEC and SPEC.loader
PROFILE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(PROFILE)


class AnalyzeQuestProfileTests(unittest.TestCase):
    def test_correlates_chunk_growth_frame_time_and_bounded_residency(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            capture = Path(temporary)
            (capture / "app-logcat.txt").write_text(
                "I Unity QIS_WORLD_PROFILE unixMs=1 reason=start chunks=1 "
                "activeRevision=0 activeState=1 edges=0 residentVolumes=1 "
                "maxResidentVolumes=1 residentMeshes=0 residentDiffSoup=0 "
                "backgroundPublications=0 allocatedBytes=100 reservedBytes=200\n"
                "I Unity QIS_TSDF_PROFILE samples=60 protection=1 "
                "cpuIntegrationAvgMs=1.25 cpuIntegrationMaxMs=2.5 "
                "cpuFrameAvgMs=10 cpuFrameMaxMs=14 gpuFrameAvgMs=9 "
                "gpuFrameMaxMs=12 tsdfBytes=32 colorBytes=64 frustumBytes=12 "
                "integrations=60\n"
                "I Unity QIS_WORLD_PROFILE unixMs=2 reason=rollover chunks=3 "
                "activeRevision=1 activeState=1 edges=2 residentVolumes=1 "
                "maxResidentVolumes=1 residentMeshes=2 residentDiffSoup=1 "
                "backgroundPublications=0 allocatedBytes=150 reservedBytes=300\n"
                "I Unity QIS_TSDF_PROFILE samples=60 protection=1 "
                "cpuIntegrationAvgMs=1.5 cpuIntegrationMaxMs=3 "
                "cpuFrameAvgMs=11 cpuFrameMaxMs=15 gpuFrameAvgMs=10 "
                "gpuFrameMaxMs=13 tsdfBytes=32 colorBytes=64 frustumBytes=12 "
                "integrations=120\n",
                encoding="utf-8",
            )
            (capture / "process-memory-before.txt").write_text(
                "TOTAL PSS: 12345 TOTAL RSS: 99999\n", encoding="utf-8"
            )
            summary = PROFILE.analyze(capture)
            self.assertEqual(summary["chunksObserved"], [1, 3])
            self.assertEqual(summary["maximumChunkCount"], 3)
            self.assertTrue(summary["residentVolumeBoundHeld"])
            self.assertEqual(summary["pairedTsdfSamples"], 2)
            self.assertEqual(summary["maximumGpuFrameAverageMs"], 10.0)
            self.assertEqual(summary["processPssKiBBefore"], 12345)
            self.assertTrue((capture / "world-profile.csv").is_file())
            self.assertTrue((capture / "tsdf-profile.csv").is_file())
            persisted = json.loads(
                (capture / "performance-summary.json").read_text(encoding="utf-8")
            )
            self.assertEqual(persisted, summary)

    def test_fails_closed_on_missing_or_malformed_markers(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            capture = Path(temporary)
            (capture / "app-logcat.txt").write_text(
                "QIS_WORLD_PROFILE unixMs=not-a-number\n", encoding="utf-8"
            )
            with self.assertRaises(PROFILE.ProfileError):
                PROFILE.analyze(capture)


if __name__ == "__main__":
    unittest.main()
