# Security Policy

## Supported versions

Only the latest release receives fixes.

## Reporting a vulnerability

If you find a security problem (for example, a CBZ file that can make the program write
files outside the chosen folder), **please do not open a public issue**.

Use GitHub's private reporting instead: go to the repository's **Security** tab and click
**Report a vulnerability**. You'll get a reply as soon as possible.

## Built-in protections

- **Zip-slip protection:** entries such as `../../Windows/file.png` are refused during extraction.
- **No network access** and no elevated (administrator) permissions.
- Original files are never modified or deleted.
