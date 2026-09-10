from pathlib import Path

bootstrap_path = Path("Services/IoTesting/IoTestWorkspaceBootstrapService.cs")
test_path = Path("tests/ARSAS.Tests/IoFatPhaseBPerIedRestoreRegressionTests.cs")

bootstrap = bootstrap_path.read_text(encoding="utf-8")
old = '''        try
        {
            if (!string.IsNullOrWhiteSpace(restoreSnapshotPath) && File.Exists(restoreSnapshotPath))
            {
                try
                {
                    ApplySnapshotProgress(importedProject, restoreSnapshotPath);
                    restored = true;

                    // The snapshot at the canonical current path is temporarily moved so
                    // persistence cannot replace the freshly imported IEC model wholesale.
                    // A compatible SCL snapshot discovered under an older staging/project
                    // identity is read-only input; the new current path is saved normally.
                    if (restoreSnapshotPath.Equals(snapshotPath, StringComparison.OrdinalIgnoreCase))
                    {
                        Directory.CreateDirectory(localDirectory);
                        File.Move(snapshotPath, backupPath, true);
                        movedSnapshot = true;
                    }
                }
                catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
                {
                    warnings.Add($"Local progress could not be restored; a fresh FAT workspace was opened: {ex.Message}");
                }
            }
'''
new = '''        try
        {
            if (!string.IsNullOrWhiteSpace(restoreSnapshotPath) && File.Exists(restoreSnapshotPath))
            {
                // Isolate the canonical persisted snapshot BEFORE attempting selective restore.
                // Otherwise a rejected/invalid partial restore could fall through to
                // IoTestWorkspacePersistence.OpenSourcesAsync(), which is allowed to restore a
                // matching snapshot wholesale. Engineering is the fresh plan authority here:
                // the old snapshot is read-only evidence input, never a replacement project.
                if (restoreSnapshotPath.Equals(snapshotPath, StringComparison.OrdinalIgnoreCase))
                {
                    Directory.CreateDirectory(localDirectory);
                    File.Move(snapshotPath, backupPath, true);
                    movedSnapshot = true;
                    restoreSnapshotPath = backupPath;
                }

                try
                {
                    ApplySnapshotProgress(importedProject, restoreSnapshotPath);
                    restored = true;
                }
                catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
                {
                    warnings.Add($"Local progress could not be restored; a fresh FAT workspace was opened: {ex.Message}");
                }
            }
'''
if bootstrap.count(old) != 1:
    raise SystemExit(f"bootstrap guard expected exactly once, found {bootstrap.count(old)}")
bootstrap = bootstrap.replace(old, new)
bootstrap_path.write_text(bootstrap, encoding="utf-8")

test = test_path.read_text(encoding="utf-8")
old_test = '''        using (reopened.Session)
        using (reopened.Workspace)
        {
            var point = reopened.Project.Ieds.Single().TestPoints.Single();
            Assert.Equal(IoTestPointState.NotStarted, point.Runtime.State);
            Assert.Null(point.Runtime.OnEvidence);
            Assert.Null(point.Runtime.OffEvidence);
        }
'''
new_test = '''        using (reopened.Session)
        using (reopened.Workspace)
        {
            // Bootstrap must keep the freshly imported Engineering plan as authority. A
            // persistence candidate may contribute progress only after IED ownership matches;
            // it must never replace the current project wholesale.
            Assert.Same(current, reopened.Project);
            var point = reopened.Project.Ieds.Single().TestPoints.Single();
            Assert.Equal(IoTestPointState.NotStarted, point.Runtime.State);
            Assert.Null(point.Runtime.OnEvidence);
            Assert.Null(point.Runtime.OffEvidence);
        }
'''
if test.count(old_test) != 1:
    raise SystemExit(f"test guard expected exactly once, found {test.count(old_test)}")
test = test.replace(old_test, new_test)
test_path.write_text(test, encoding="utf-8")
