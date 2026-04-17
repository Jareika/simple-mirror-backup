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
 - Compare to preview changes
 - Mirror to make the target identical to the source
 - Synchronize to copy newer files in both directions
 - Backup to copy only new/changed files to the target
 - Click Save to store the job list (It auto saves normally)

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