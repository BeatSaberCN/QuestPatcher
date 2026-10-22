# QuestPatcher.Zip

An implementation of the ZIP file format that has built-in support for APK signing with a v2 signature.

## Features
- Creating, deleting and overwriting files.
- Signing with a v2 signature. Existing v1 signature files are left untouched.
- Modifies file in-place, i.e. no reading into memory. This minimises memory usage.
- Maintains file alignment, i.e. aligning the APK after closing it is not necessary.

## Limitations
- When a file is removed or overwritten in the APK, the previous file data is not removed, and is still present at some location inside the archive. This isn't much of a problem for patching quest apps, since we don't replace any files of significant size.
- Not thread safe.