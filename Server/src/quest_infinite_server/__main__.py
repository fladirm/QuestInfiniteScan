from __future__ import annotations

import os

import uvicorn


def main() -> None:
    uvicorn.run(
        "quest_infinite_server.api:create_app",
        factory=True,
        host=os.environ.get("QIS_SERVER_HOST", "127.0.0.1"),
        port=int(os.environ.get("QIS_SERVER_PORT", "8420")),
        log_level=os.environ.get("QIS_SERVER_LOG_LEVEL", "info"),
    )


if __name__ == "__main__":
    main()
