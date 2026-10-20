# CI P4 documentation-only smoke fixture

This file exists only to verify that a purely documentary PR based on CI P4 executes the cheap fail-closed PR scope classifier while **not** queuing the canonical Windows build. The behavior is probed separately from the real source-changing PR #653, which still must run full Windows CI. This temporary PR will be closed after check evidence is recorded. No source, engine or release authority changed.
