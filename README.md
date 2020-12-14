[nvm]: https://github.com/nvm-sh/nvm
[volta]: https://volta.sh/
[nvm-windows]: https://github.com/coreybutler/nvm-windows

# NvmFs

> Node Version Manager F#
> Get the binaries at the [releases](https://github.com/AngelMunoz/NvmFs/releases) tab
>
> if you have the dotnet-sdk installed run  `dotnet tool install -g NvmFs`

A Node version Manager Written in F#

This is probably the simplest Node Version Manager you'll find it doesn't have a lot of features and this is on purpose for more complete solutions please take a look at [nvm], [volta], [nvm-windows].

## Why would you want this?

If you want a dead simple node version manager this is for you.

Also, this tool is distributed in binary form as well as a `dotnet tool` so if you're running on CI with dotnet available and depend on installing a specific node version, perhaps this is the reason you may want it above the others.


## Why would you not want this?

> tl;dr: You need something else rather than just `nvmfs install version` or `nvmfs use version`

- You need to run commands with different versions of node in the current shell without setting a global default [like this](https://docs.volta.sh/reference/run).

- You need to reinstall packages between versions [like this](https://github.com/nvm-sh/nvm#migrating-global-packages-while-installing)

- You need different architectures [like this](https://github.com/coreybutler/nvm-windows#usage)

## Misc. Info

We don't handle existing node instalations outside the `NVMFS_HOME` directory

- Windows

  - We use the user's system environment variables, if your powershell/cmd session is not recognizing node, then you need to close it and open it again, in the worst case you just need to log off and log in back to your account
  - we use cmd's `mklink` to create symlinks on windows
    > Please Note that this requires developer mode turned on on [windows 10](https://blogs.windows.com/windowsdeveloper/2016/12/02/symlinks-windows-10/)
  - we use cmd's `rmdir` to remove symlinks

- Unix
  - We append environment variables to the PATH using the `~/.bashrc`
  - We use `ln -s` to create symlinks
  - We use `unlink` to remove symlinks
  - We use `chmod +x` to set execute permissions once we symlink the selected version

## Customization

You can customize paths using the following environment variables

- `NVMFS_HOME` - where is nvmfs going to download node versions, defaults to

  > $"{Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)}/nvmfs"

  - `C:\Users\username\AppData\Roaming\nvmfs` on windows
  - `/home/username/.config/nvmfs` on linux/macos

- `NVMFS_NODE` - symlinked location that will be added to the PATH
  - `%%NVMFS_HOME%%\bin` on windows
  - `$NVMFS_HOME/bin` on linux/macos
- `NVM_SOURCE_BASE_URL` - the base url to get node distributions, (defaults to https://nodejs.org/dist)

### nvmfs help

```
NvmFs 0.5.0
Copyright (C) 2020 Angel D. Munoz

  install      Installs the specified node version or the latest LTS by default

  use          Sets the Node Version

  list         Shows the available node versions

  uninstall    Uninstalls the specified node version

  help         Display more information on a specific command.

  version      Display version information.
```

### nvmfs install --help

```
NvmFs 0.5.0
Copyright (C) 2020 Angel D. Munoz

  -n, --node       (Group: version) Installs the specified node version

  -l, --lts        (Group: version) Ignores version and pulls down the latest LTS version

  -c, --current    (Group: version) Ignores version and pulls down the latest Current version

  -d, --default    Sets the downloaded version as default (default: false)

  --help           Display this help screen.

  --version        Display version information.
```

### nvmfs use --help

```
NvmFs 0.5.0
Copyright (C) 2020 Angel D. Munoz

  -n, --node       (Group: version) sets the specified node version in the PATH

  -l, --lts        (Group: version) Ignores version and sets the latest downloaded LTS version in the PATH

  -c, --current    (Group: version) Ignores version and sets the latest downloaded Current version in the PATH

  --help           Display this help screen.

  --version        Display version information.
```

### nvmfs list --help

```
NvmFs 0.5.0
Copyright (C) 2020 Angel D. Munoz

  -r, --remote    Displays the last downloaded version index in the console

  -u, --update    Use together with --remote, pulls the version index from the node website

  --help          Display this help screen.

  --version       Display version information.
```

### nvmfs uninstall --help

```
NvmFs 0.5.0
Copyright (C) 2020 Angel D. Munoz

  -n, --node    Required. Removes the specified node version

  --help        Display this help screen.

  --version     Display version information.
```
