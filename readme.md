# Simple Mirror Backup

<img width="1657" height="978" alt="image" src="https://github.com/user-attachments/assets/a5fa9d42-39d4-4201-b7f5-a28053842da1" />

Simple Mirror Backup is a lightweight Windows 11 desktop application for folder-based backups, mirroring, and synchronization.

It is designed for users who want a clear and predictable tool for copying files between two folders, previewing planned actions before execution, and organizing backup jobs in a simple UI.

## Features

- **Mirror mode**
  - Copies new and changed files from source to target
  - Deletes files and folders in the target that no longer exist in the source

- **Backup mode**
  - Copies new and changed files from source to target
  - Does **not** delete extra files or folders in the target

- **Synchronize mode**
  - Compares both sides
  - Copies newer or missing files in both directions
  - Skips file/folder conflicts and reports them as warnings

- **Comparison preview**
  - Build a plan before execution
  - Review copy and delete actions
  - Deselect actions manually before running
  - Remember deselected entries for future comparisons

- **Folder exclusion tree**
  - Load subfolders from the source path
  - Uncheck folders that should not be scanned or processed

- **Job organization**
  - Create, copy, rename, and delete jobs
  - Group jobs into tiles and folders
  - Reorder tiles by drag & drop
  - Move jobs between tiles and folders by drag & drop

- **Remote device support**
  - Wake-on-LAN
  - SSH console launcher
  - Remote shutdown command
  - Optional NAS startup countdown indicator

- **Language support**
  - UI text loaded from JSON language files

- **Persistent UI state**
  - Saves jobs, tiles, folder structure, remembered comparison selections, and window layout

## Requirements

- Windows 11
- .NET 8 Desktop Runtime  
  or publish as self-contained executable

## Project Type

- C#
- .NET 8
- Windows Forms

## Build

### Run from source

```bash
dotnet build
dotnet run
````

### Publish as single-file Windows executable

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o .\dist\SimpleMirrorBackup-Single
```

## Data Storage

The application stores its data in:

```text
Data\jobs.json
```

relative to the application directory.

This file contains:

- backup jobs
- tiles
- folder structure
- remote device settings
- UI settings
- remembered comparison selections

## Main Concepts

### 1. Jobs

A job defines:

- a name
- a source folder
- a target folder
- optional excluded subfolders
- an optional tile/folder location in the UI

### 2. Tiles

Tiles are top-level visual groups for jobs. They can be used to separate jobs by purpose, device, customer, or location.

### 3. Folder Tree Exclusions

After selecting a source folder, the app can load its subfolder structure. Unchecked folders are treated as excluded and are not scanned during compare or run operations.

### 4. Comparison Plans

Before executing a job, you can open a comparison preview.

The preview shows:

- action type
- relative path
- source state
- target state
- details

From there you can:

- search entries
- deselect entries
- reset all selections
- run only the selected actions

## Backup Modes

### Mirror

**Source -> Target**

Use this when the target should become an exact mirror of the source.

Behavior:

- copies missing files
- updates changed files
- deletes extra files/folders in target

### Backup

**Source -> Target**

Use this when the target should receive new and changed files, but existing extra data in the target should be kept.

Behavior:

- copies missing files
- updates changed files
- does not delete extras in target

### Synchronize

**Source <-> Target**

Use this when both locations may have changed.

Behavior:

- copies files to the other side when missing
- if both files exist but differ, the newer timestamp wins
- if timestamps are equal but files differ by metadata logic, source wins
- file/folder conflicts are skipped with warnings

## Safety Notes

Please read carefully before using:

- **Mirror mode can delete files and folders in the target.**
- Always verify source and target paths before running.
- Use the **comparison preview** before executing important jobs.
- Test with non-critical folders first.
- The application compares files primarily by:
    - file size
    - last write time (UTC)

It does **not** currently perform content hashing.

## Reparse Points / Links

Directory reparse points are skipped during folder enumeration to avoid accidental recursion or traversing linked locations.

During deletion, reparse points are deleted as links, not recursively traversed.

## Remote Device Functions

The application can optionally interact with a remote NAS or similar device.

### Wake-on-LAN

Requires:

- MAC address
- optional broadcast address
- optional custom WoL port

### SSH

Requires:

- SSH host
- SSH port
- username
- optional password

### Shutdown

Requires:

- valid SSH settings
- a shutdown command

Default command:

```bash
nohup sudo /sbin/shutdown -h now >/dev/null 2>&1 &
```

You may need to configure your remote system so that this command works without interactive password prompts.

## Localization

Language files are loaded from:

```text
Language\*.json
```

Each file contains translated UI strings.

Current included languages:

- English
- German

## Known Behavior

- Folder tree loading is based on the current source folder
- If the source path changes, exclusions are reset until the folder tree is loaded again
- Comparison deselections can be remembered per job and per backup mode
- Read errors in folders are reported as warnings and affected branches may be skipped

## Dependencies

- [SSH.NET](https://github.com/sshnet/SSH.NET)

NuGet package used:

- `SSH.NET`

## Example Workflow

1. Create a new job
2. Enter source and target paths
3. Click **Load folders**
4. Uncheck folders you want to exclude
5. Click **Compare**
6. Review planned actions
7. Deselect anything you do not want to execute
8. Start the selected actions

## Intended Use

Simple Mirror Backup is intended for:

- personal backups
- local or network folder replication
- simple NAS copy jobs
- controlled manual backup workflows

It is intentionally focused on transparency and direct folder operations rather than advanced enterprise backup features.

## Not Included

This tool currently does not provide:

- versioned backups
- compression
- encryption
- scheduling
- cloud integration
- block-level deduplication
- hash-based verification
- VSS/open-file snapshot support

## License

Add your preferred license here.

For example:

```text
MIT License
```

## Status

This is a desktop utility focused on simple, understandable backup workflows for Windows 11. Use with care, especially when running mirror operations that include deletions.
