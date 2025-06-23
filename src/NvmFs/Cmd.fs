namespace NvmFs.Cmd

open System
open System.Threading.Tasks
open Spectre.Console
open FsToolkit.ErrorHandling
open NvmFs

[<RequireQualifiedAccess>]
module Actions =
  open System.IO

  let private validateVersion(num: string) =
    if num.IndexOf('v') = 0 then
      let (parsed, _) = num.Substring 1 |> System.Int32.TryParse
      parsed
    else
      let (parsed, _) = System.Int32.TryParse(num)
      parsed

  let private validateVersionGroup
    (version: string option, lts: bool, current: bool)
    =
    if
      [ version.IsSome; lts; current ] |> List.filter id |> List.length > 1
    then
      failwith "Can only have one of 'version', '--lts' or '--current'."

  let private getInstallType
    (isLts: bool)
    (isCurrent: bool)
    (version: string option)
    : Result<InstallType, string> =

    match isLts, isCurrent, version with
    | _, _, Some version ->
      match version.Split(".") with
      | [| major |] ->
        if validateVersion major then
          Ok(SpecificM major)
        else
          Error $"{version} is not a valid node version"
      | [| major; minor |] ->
        if validateVersion major && validateVersion minor then
          Ok(SpecificMM(major, minor))
        else
          Error $"{version} is not a valid node version"
      | [| major; minor; patch |] ->
        if
          validateVersion major
          && validateVersion minor
          && validateVersion patch
        then
          Ok(SpecificMMP(major, minor, patch))
        else
          Error $"{version} is not a valid node version"
      | _ -> Error $"{version} is not a valid node version"
    | true, _, _ -> Ok LTS
    | _, true, _ -> Ok Current
    | _ ->
      Error
        $"Use only one of --lts 'boolean', --current 'boolean', or --version 'string'"

  let private setVersionAsDefault (version: string) (os: CurrentOS) = result {
    let! symlink = Env.setEnvVersion os version |> Result.mapError SymlinkError

    match os with
    | Windows -> ()
    | Mac
    | Linux ->
      AnsiConsole.MarkupLine("[yellow]Setting permissions for node[/]")
      do! IO.trySetPermissionsUnix symlink
    | FreeBSD -> return! Error UnsuppoertdOS
  }

  let private runPreInstallChecks() =
    let homedir = IO.createHomeDir()
    AnsiConsole.MarkupLine("[yellow]Updating node versions[/]")

    task {
      let! file = Network.downloadNodeVersions(homedir.FullName)

      match file with
      | Ok _ -> AnsiConsole.MarkupLine("[green]Updated node versions[/]")
      | Error e ->
        AnsiConsole.MarkupLine(
          $"[red]Failed to update node versions[/]: {e.Message.EscapeMarkup()}"
        )
    }

  let private downloadNode (version: NodeVerItem) (setDefault: bool) = taskResult {
    let! _, os, arch =
      Common.getOsArchCodename version |> Result.setError PlatformError

    let! node =
      AnsiConsole
        .Status()
        .StartAsync(
          $"Downloading Node [yellow]{version.version}[/]",
          fun _ -> Network.downloadNode version.version os arch
        )
      |> TaskResult.mapError(fun error -> FailedToSetDefault error.Message)

    if setDefault then
      do!
        setVersionAsDefault version.version os
        |> Result.mapError(fun err -> FailedToSetDefault err.Value)

    return node
  }

  let Install
    (version: string option, lts: bool, current: bool, isDefault: bool)
    =
    task {
      validateVersionGroup(version, lts, current)
      do! runPreInstallChecks()

      let versions = IO.getIndex()

      match getInstallType lts current version, isDefault with
      | Ok install, setAsDefault ->

        let! installResult =
          taskResult {
            let! version =
              Common.getVersionItem versions install
              |> Result.requireSome(VersionNotFound install.asString)

            do! downloadNode version setAsDefault |> TaskResult.ignore

            AnsiConsole.MarkupLine
              $"[green]Node version {version.version} installed[/]"

            if setAsDefault then
              AnsiConsole.MarkupLine
                $"[green]Set {version.version} as default[/]"

            return ()
          }
          |> TaskResult.mapError(fun error ->
            match error with
            | VersionNotFound version -> $"[red]{version}[/] not found"
            | FailedToSetDefault msg ->
              $"[red]We could not set {install.asString} as default: {msg}[/]"
            | PlatformError ->
              $"[red]We were unable to get the current platform and architecture[/]")

        match installResult with
        | Ok() ->
          AnsiConsole.MarkupLine ""
          return 0
        | Error err ->
          AnsiConsole.Markup err
          return 1
      | Error err, _ ->
        AnsiConsole.MarkupLine $"[red]{err}[/]"
        return 1
    }

  let findAndSetVersion version = result {
    let! _, os, _ =
      Common.getOsArchCodename version |> Result.setError UseError.PlatformError

    do!
      IO.getLocalNodes()
      |> Array.contains version.version
      |> Result.requireTrue(UseError.NodeNotInDisk version.version)

    AnsiConsole.MarkupLine
      $"[yellow]Setting version[/] [green]%s{version.version}[/]"

    do!
      setVersionAsDefault version.version os
      |> Result.mapError(fun err -> UseError.FailedToSetDefault err.Value)
  }

  let Use(version: string option, lts: bool, current: bool) = task {
    validateVersionGroup(version, lts, current)
    AnsiConsole.MarkupLine $"[yellow]Checking local versions[/]"

    let versions = IO.getIndex()

    match getInstallType lts current version with
    | Ok install ->
      let version = Common.getVersionItem versions install

      match version with
      | Some version ->

        let findAndSetResult = findAndSetVersion version

        match findAndSetResult with
        | Error UseError.PlatformError ->
          AnsiConsole.MarkupLine
            $"[red]We were unable to get the current platform and architecture[/]"

          return 1
        | Error(UseError.FailedToSetDefault msg) ->
          AnsiConsole.MarkupInterpolated
            $"[yellow]We could not set {version.version} as default: ${msg}[/]"

          return 1
        | Error(UseError.NodeNotInDisk codename) ->
          let l2 = $"[yellow]%s{version.version}[/]"

          AnsiConsole.MarkupLine
            $"We weren't able to find {l2} locally, please use 'install' and optionally '--default' to enable it."

          return 1
        | Ok _ ->
          AnsiConsole.MarkupLine
            $"[green]Node version {version.version} set as the default[/]"

          return 0
      | None ->
        AnsiConsole.MarkupLine "[red]Version Not found[/]"
        return 1
    | Error err ->
      AnsiConsole.MarkupLine $"[red]{err}[/]"
      return 1
  }

  let doUninstall version = result {
    let! codename, os, arch =
      Common.getOsArchCodename version
      |> Result.setError UninstallError.PlatformError

    do!
      IO.codenameExistsInDisk codename
      |> Result.requireTrue UninstallError.NodeNotInDisk

    AnsiConsole.MarkupLine
      $"[yellow]Uninstalling version[/]: %s{version.version}"

    let path =
      Path.Combine(
        Common.getHome(),
        $"latest-{codename}",
        Common.getVersionDirName version.version os arch
      )

    AnsiConsole.MarkupLine
      $"[yellow]Uninstalling version[/]: %s{version.version}"

    try
      let target = Directory.ResolveLinkTarget(path, true)
      Directory.Delete(target.LinkTarget)
      Directory.Delete(path, true)
      return ()
    with ex ->
      return! UninstallError.FailedToDelete ex |> Result.Error
  }

  let Uninstall(version: string option) = task {
    let versions = IO.getIndex()

    match getInstallType false false version with
    | Ok install ->
      let version = Common.getVersionItem versions install

      match version with
      | Some version ->
        match doUninstall version with
        | Ok _ ->
          AnsiConsole.MarkupLine
            $"[green]Uninstalled Version[/] [yellow]%s{version.version}[/][green] successfully[/]"

          return 0
        | Error UninstallError.PlatformError ->
          AnsiConsole.MarkupLine
            $"[red]We were unable to get the current platform and architecture[/]"

          return 1
        | Error UninstallError.NodeNotInDisk ->
          let l1 = $"[red]Version[/] [yellow]%s{version.version}[/]"

          let l2 = $"[red]is not present in the system, aborting.[/]"

          AnsiConsole.MarkupLine $"{l1} {l2}"
          return 1

        | Error(UninstallError.FailedToDelete ex) ->
          AnsiConsole.MarkupLine $"[red]Failed to delete {version.version}[/]"
#if DEBUG
          AnsiConsole.WriteException(ex, ExceptionFormats.ShortenPaths)
#endif
          return 1
      | None ->
        AnsiConsole.MarkupLine "[red]Version Not found[/]"
        return 1
    | Result.Error err ->
      AnsiConsole.MarkupLine $"[red]{err}[/]"
      return 1
  }

  let List(remote: bool option, updateIndex: bool option) = task {
    let checkRemote = remote |> Option.defaultValue false
    let updateIndex = checkRemote && (updateIndex |> Option.defaultValue false)

    let! currentVersion =
      taskResult {
        let! os = Common.getOSPlatform()
        return! IO.getCurrentNodeVersion os
      }
      |> TaskResult.defaultValue ""

    let getVersionsTable
      (localVersions: string[])
      (remoteVersions: string[] option)
      =
      let remoteVersions = defaultArg remoteVersions [||]

      let table =
        Table()
          .AddColumns(
            [|
              TableColumn("Local")
              if remoteVersions.Length > 0 then
                TableColumn("Remote")
            |]
          )

      table.Title <- TableTitle("Node Versions:")

      table.Width <- 75

      let longestLength =
        if localVersions.Length > remoteVersions.Length then
          localVersions.Length
        else
          remoteVersions.Length

      let markCurrent(version: string) =
        if
          not(String.IsNullOrEmpty currentVersion)
          && version.Contains(currentVersion)
        then
          $"[green]*[/] {version}"
        else
          $"{version}"

      for i in 0 .. longestLength - 1 do
        let localVersion =
          localVersions |> Array.tryItem i |> Option.defaultValue ""

        table.AddRow [|
          markCurrent localVersion
          if remoteVersions.Length > 0 then
            markCurrent(
              remoteVersions |> Array.tryItem i |> Option.defaultValue ""
            )
        |]
        |> ignore

      table.Caption <-
        TableTitle($"Active version is marked with '[green]*[/]'")

      table

    let! local, remote = task {
      match checkRemote, updateIndex with
      | true, true ->
        do! runPreInstallChecks()
        let nodes = IO.getIndex()

        return
          IO.getLocalNodes(), Some(nodes |> Array.map(fun ver -> ver.version))
      | true, false ->
        AnsiConsole.MarkupLine "[yellow]Checking local versions[/]"

        let nodes = IO.getIndex()

        return
          IO.getLocalNodes(), Some(nodes |> Array.map(fun ver -> ver.version))
      | false, true ->
        AnsiConsole.MarkupLine
          "[yellow]Warning[/]: --update can only be used when [yellow]--remote true[/] is set, ignoring."

        return IO.getLocalNodes(), None
      | _ -> return IO.getLocalNodes(), None
    }

    let table = getVersionsTable local remote
    AnsiConsole.Write table
    return 0
  }
