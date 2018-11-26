# DokanPBO

## Features

**DokanPbo** can be used to create a virtual "P drive" without ever unpacking PBO's or wasting disk space.

It can directly mount the folder structure inside the PBO as a Directory or Drive.

## Setup

- Install [Dokan](https://github.com/dokan-dev/dokany/releases)
- Compile and install DokanPbo using Visual Studio or other .NET Framework capable compiler
  - Requires at least .NET Framework **4.5**!

## Usage

Run DokanPbo from the command line.

```
-f, --folders    Required. Directories with PBO files to mount.
-o, --output     Required. Drive or directory where to mount.
--prefix         Prefix used to filter PBO paths.
--excludePrefix  Prefix used to exclude PBO files.
-u, --unmount    Drive or directory to unmount.
--help           Display this help screen.
```

`--prefix` is a inclusion filter. Everything that doesn't match is ignored.
`--excludePrefix` is a exclusion filter. Everything that matches it is ignored.
`--excludePrefix \z\ace` ignores all files out of PBO's that would be in the `\z\ace` path. The starting `\` is important.

**Example**

```
DokanPbo -f "<Armapath>\addons" "<Armapath>\Heli\addons" "<Armapath>\Expansion\addons" "<Armapath>\@CBA_A3\addons" -o P:
```

This example will create a new virtual disk drive on letter `P`.