# Archive test files

Small archives the tests open (see `tests/Wheel.Core.Tests/Program.cs`, "Archives").

- `Rar.rar`, `Rar5.rar`, `Rar5.encrypted_filesOnly.rar` (password `test`): from SharpCompress's test set,
  <https://github.com/adamhathcock/sharpcompress/tree/master/tests/TestArchives/Archives> (MIT licence).
- Everything else holds `hello.txt` and `docs/notes.md`, made with py7zr, pycdlib, tar, gzip, bzip2 and xz.
  `encrypted.7z` and `encrypted-names.7z` use the password `secret`.
- `zip-slip.zip` tries to write outside its folder (`../escaped.txt`, `/absolute.txt`); extraction must skip both.
- `aes-other-tool.zip` (AES, made with pyzipper) and `zipcrypto.zip` (the old ZIP encryption, made with Info-ZIP `zip -P`)
  use the password `secret`: Desk Wheel must open password ZIPs made by other apps, not just its own.
