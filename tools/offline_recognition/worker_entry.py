"""Frozen CLI entry point. The application passes screenshot and data paths."""

import sys

from tools.offline_recognition.cli import main


if __name__ == "__main__":
    sys.exit(main())
