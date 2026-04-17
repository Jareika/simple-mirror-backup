# Simple Mirror Backup

Simple Mirror Backup is a small Windows 11 desktop app for fast folder-based backups.

It supports three modes:

- **Mirror**  
  Copies new and changed files from source to target and deletes files/folders in the target that no longer exist in the source.

- **Synchronize**  
  Compares both sides and keeps the newer version of a file.  
  No deletions are performed in this mode.

- **Backup**  
  Copies only new or changed files from source to target.  
  Existing extra files in the target are kept.

## Features

- Windows Forms app for Windows 11
- Fast folder comparison
- Compare before running a job (Attention! It does not compare automatically before a job)
- Exclude subfolders by unchecking them in the folder tree
- Save and reuse backup jobs
- Colored comparison preview
  - Green = copy/update
  - Red = delete

## Requirements

- Windows 11

## Build

```bash
dotnet build
Run
dotnet run
Or open the solution/project in Visual Studio and start it there.
```

### Build Requirements
- .NET 8 SDK or .NET 8 Desktop Runtime

# How to use

1. Create a new job
2. Select a source folder
3. Select a target folder
4. Click Load folders
5. Uncheck subfolders you want to exclude
6. Use one of the following:
7. Compare to preview changes
8. Mirror to make the target identical to the source
9. Synchronize to copy newer files in both directions
10. Backup to copy only new/changed files to the target
11. Click Save to store the job list

# Data storage
Jobs are stored in:

Data/jobs.json
next to the application.

# Notes
- Reparse points/symlinks are skipped during folder scanning.
- Synchronize mode does not propagate deletions.
- Always test with non-critical data first.

# License
This project is licensed under the MIT License. See the LICENSE file for details.