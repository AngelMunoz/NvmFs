namespace NvmFs.Cmd

open System
open System.Threading.Tasks
open FSharp.Control.Tasks
open Spectre.Console
open CommandLine
open NvmFs


[<Verb("install", HelpText = "Installs the specified node version or the latest LTS by default")>]
type Install =
    { [<Option('n', "node", Group = "version", HelpText = "Installs the specified node version")>]
      version: string
      [<Option('l', "lts", Group = "version", HelpText = "Ignores version and pulls down the latest LTS version")>]
      lts: Nullable<bool>
      [<Option('c', "current", Group = "version", HelpText = "Ignores version and pulls down the latest Current version")>]
      current: Nullable<bool>
      [<Option('d', "default", Required = false, HelpText = "Sets the downloaded version as default (default: false)")>]
      isDefault: Nullable<bool> }

[<Verb("uninstall", HelpText = "Uninstalls the specified node version")>]
type Uninstall =
    { [<Option('n', "node", Required = true, HelpText = "Removes the specified node version")>]
      version: string }

[<Verb("use", HelpText = "Sets the Node Version")>]
type Use =
    { [<Option('n', "node", Group = "version", HelpText = "sets the specified node version in the PATH")>]
      version: string
      [<Option('l',
               "lts",
               Group = "version",
               HelpText = "Ignores version and sets the latest downloaded LTS version in the PATH")>]
      lts: Nullable<bool>
      [<Option('c',
               "current",
               Group = "version",
               HelpText = "Ignores version and sets the latest downloaded Current version in the PATH")>]
      current: Nullable<bool> }

[<Verb("list", HelpText = "Shows the available node versions")>]
type List =
    { [<Option('r', "remote", Required = false, HelpText = "Displays the last downloaded version index in the console")>]
      remote: Nullable<bool>
      [<Option('u',
               "update",
               Required = false,
               HelpText = "Use together with --remote, pulls the version index from the node website")>]
      updateIndex: Nullable<bool> }

[<RequireQualifiedAccess>]
module Actions =
    let private validateVersion (num: string) =
        if num.IndexOf('v') = 0 then
            let (parsed, _) = num.Substring 1 |> System.Int32.TryParse
            parsed
        else
            let (parsed, _) = System.Int32.TryParse(num)
            parsed

    let private getInstallType (isLts: Nullable<bool>)
                               (isCurrent: Nullable<bool>)
                               (version: string)
                               : Result<InstallType, string> =
        let isLts = isLts |> Option.ofNullable
        let isCurrent = isCurrent |> Option.ofNullable
        let version = version |> Option.ofObj

        match isLts, isCurrent, version with
        | Some lts, None, None ->
            if lts
            then Ok LTS
            else Result.Error "No valid version was presented"
        | None, Some current, None ->
            if current
            then Ok Current
            else Result.Error "No valid version was presented"
        | None, None, Some version ->
            match version.Split(".") with
            | [| major |] ->
                if validateVersion major
                then Ok(SpecificM major)
                else Result.Error $"{version} is not a valid node version"
            | [| major; minor |] ->
                if validateVersion major && validateVersion minor
                then Ok(SpecificMM(major, minor))
                else Result.Error $"{version} is not a valid node version"
            | [| major; minor; patch |] ->
                if validateVersion major
                   && validateVersion minor
                   && validateVersion patch then
                    Ok(SpecificMMP(major, minor, patch))
                else
                    Result.Error $"{version} is not a valid node version"
            | _ -> Result.Error $"{version} is not a valid node version"
        | _ -> Result.Error $"Use only one of --lts 'boolean', --current 'boolean', or --version 'string'"

    let private setVersionAsDefault (version: string)
                                    (codename: string)
                                    (os: string)
                                    (arch: string)
                                    : Task<Result<unit, string>> =
        task {
            let directory = Common.getVersionDirName version os arch

            let symlinkpath = IO.getSymlinkPath codename directory os

            match Env.setEnvVersion os symlinkpath with
            | Ok _ ->
                AnsiConsole.MarkupLine("[yellow]Setting permissions for node[/]")

                match os with
                | "win" -> return Ok()
                | _ ->
                    let! result = IO.trySetPermissionsUnix symlinkpath

                    if result.ExitCode <> 0 then
                        let errors =
                            result.Errors
                            |> List.fold (fun value next -> $"{value}\n{next}") ""

                        return Result.Error($"[red]Error while setting permissions[/]: {errors}")
                    else
                        return Ok()
            | Error err -> return Result.Error err
        }

    let private runPreInstallChecks () =
        let homedir = IO.createHomeDir ()
        AnsiConsole.MarkupLine("[bold yellow]Updating node versions[/]")

        task {
            let! file = Network.downloadNodeVersions (homedir.FullName)
            AnsiConsole.MarkupLine($"[green]Updated node versions on {file}[/]")
        }
        :> Task

    let Install (options: Install) =
        task {
            do! runPreInstallChecks ()

            let! versions = IO.getIndex ()

            match getInstallType options.lts options.current options.version,
                  (Option.ofNullable options.isDefault
                   |> Option.defaultValue false) with
            | Ok install, setAsDefault ->
                let version = Common.getVersionItem versions install

                match version with
                | Some version ->
                    let os = Common.getOS ()
                    let arch = Common.getArch ()

                    let codename = Common.getCodename version

                    let! checksums = Network.downloadChecksumsForVersion $"{version.version}"
                    let! node = Network.downloadNode $"{version.version}" version.version os arch
                    AnsiConsole.MarkupLine $"[#5f5f00]Downloaded[/]: {checksums} - {node}"

                    match IO.getChecksumForVersion checksums version.version os arch with
                    | Some checksum ->
                        if not (IO.verifyChecksum node checksum) then
                            let compares =
                                $"download: {IO.getChecksumForFile node}]\nchecksum: {checksum}"

                            AnsiConsole.MarkupLine $"[bold red]The Checksums didnt match\n{compares}[/]"
                            return 1
                        else
                            let what = $"[yellow]{node}[/]"

                            let target =
                                $"[yellow]{Common.getHome ()}/latest-{codename}[/]"

                            AnsiConsole.MarkupLine $"[#5f5f00]Extracting[/]: {what} to {target}"

                            IO.extractContents os node (IO.fullPath (Common.getHome (), [ $"latest-{codename}" ]))

                            AnsiConsole.MarkupLine "[green]Extraction Complete![/]"

                            let tryClean () =
                                try
                                    let dirname = IO.getParentDir checksums
                                    IO.deleteFile node
                                    IO.deleteFile checksums
                                    IO.deleteDir dirname
                                with ex ->
#if DEBUG
                                    AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything)
#endif
                                ()

                            if setAsDefault then
                                let! result = setVersionAsDefault version.version codename os arch

                                match result with
                                | Ok () ->
                                    AnsiConsole.MarkupLine
                                        $"[bold green]Node version {version.version} installed and set as default[/]"

                                    tryClean ()
                                    return 0
                                | Error err ->
                                    AnsiConsole.MarkupLine err
                                    return 1
                            else
                                return 0
                    | None ->
                        AnsiConsole.MarkupLine
                            $"[bold red]The Checksums didnt match\ndownload: {IO.getChecksumForFile node}\nchecksum: None[/]"

                        return 1
                | None ->
                    AnsiConsole.MarkupLine "[bold red]Version Not found[/]"
                    return 1
            | Result.Error err, _ ->
                AnsiConsole.MarkupLine $"[bold red]{err}[/]"
                return 1
        }


    let Use (options: Use) =
        task {
            AnsiConsole.MarkupLine $"[bold yellow]Checking local versions[/]"

            let! versions = IO.getIndex ()

            match getInstallType options.lts options.current options.version with
            | Ok install ->
                let version = Common.getVersionItem versions install

                match version with
                | Some version ->

                    let os = Common.getOS ()
                    let arch = Common.getArch ()

                    let codename = Common.getCodename version

                    if not (IO.codenameExistsInDisk codename) then
                        let l1 = "[bold red]We didn't find version[/]"
                        let l2 = $"[bold yellow]%s{version.version}[/]"
                        AnsiConsole.MarkupLine $"{l1} {l2} within [bold yellow]%s{codename}[/]"
                        return 1
                    else
                        AnsiConsole.MarkupLine $"[bold yellow]Setting version[/] [green]%s{version.version}[/]"

                        let! result = setVersionAsDefault version.version codename os arch

                        match result with
                        | Ok () ->
                            AnsiConsole.MarkupLine $"[bold green]Node version {version.version} set as the default[/]"
                        | Error err -> AnsiConsole.MarkupLine err

                        return 0
                | None ->
                    AnsiConsole.MarkupLine "[bold red]Version Not found[/]"
                    return 1
            | Error err ->
                AnsiConsole.MarkupLine $"[bold red]{err}[/]"
                return 1
        }

    let Uninstall (options: Uninstall) =
        task {
            let! versions = IO.getIndex ()

            match getInstallType (Nullable<bool>()) (Nullable<bool>()) options.version with
            | Ok install ->
                let version = Common.getVersionItem versions install

                match version with
                | Some version ->

                    let os = Common.getOS ()
                    let arch = Common.getArch ()

                    let codename = Common.getCodename version

                    if not (IO.versionExistsInDisk codename version.version) then
                        let l1 =
                            $"[red]Version[/] [bold yellow]%s{version.version}[/]"

                        let l2 =
                            $"[red]is not present in the system, aborting.[/]"

                        AnsiConsole.MarkupLine $"{l1} {l2}"
                        return 1
                    else
                        AnsiConsole.MarkupLine $"[yellow]Uninstalling version[/]: %s{version.version}"

                        let path =
                            IO.fullPath
                                (Common.getHome (),
                                 [ $"latest-{codename}"
                                   Common.getVersionDirName version.version os arch ])

                        try
                            IO.deleteDir path

                            AnsiConsole.MarkupLine
                                $"[green]Uninstalled Version[/] [bold yellow]%s{version.version}[/][green] successfully[/]"

                            return 0
                        with ex ->
                            AnsiConsole.MarkupLine $"[bold red]Failed to delete {version.version}[/]"
#if DEBUG
                            AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything)
#endif
                            return 1
                | None ->
                    AnsiConsole.MarkupLine "[bold red]Version Not found[/]"
                    return 1
            | Result.Error err ->
                AnsiConsole.MarkupLine $"[bold red]{err}[/]"
                return 1
        }

    let List (options: List) =
        task {
            let checkRemote =
                options.remote
                |> Option.ofNullable
                |> Option.defaultValue false

            let updateIndex =
                checkRemote
                && (options.updateIndex
                    |> Option.ofNullable
                    |> Option.defaultValue false)

            let! currentVersion =
                task {
                    let! result = IO.getCurrentNodeVersion (Common.getOS ())
                    if result.ExitCode <> 0 then return "" else return result.StandardOutput.Trim()
                }

            let getVersionsTable (localVersions: string []) (remoteVersions: string [] option) =
                let remoteVersions = defaultArg remoteVersions [||]

                let table =
                    Table()
                        .AddColumns([| TableColumn("Local")
                                       if remoteVersions.Length > 0 then TableColumn("Remote") |])

                table.Title <- TableTitle("Node Versions\n[green]* currently set as default[/]")

                let longestLength =
                    if localVersions.Length > remoteVersions.Length
                    then localVersions.Length
                    else remoteVersions.Length

                let markCurrent (version: string) =
                    if version.Contains(currentVersion) then $"[green]{version}*[/]" else version

                for i in 0 .. longestLength - 1 do
                    let localVersion =
                        localVersions
                        |> Array.tryItem i
                        |> Option.defaultValue ""

                    table.AddRow
                        ([| markCurrent localVersion
                            if remoteVersions.Length > 0 then
                                markCurrent
                                    (remoteVersions
                                     |> Array.tryItem i
                                     |> Option.defaultValue "") |])
                    |> ignore

                table

            let! local, remote =
                task {
                    match checkRemote, updateIndex with
                    | true, true ->
                        do! runPreInstallChecks ()
                        let! nodes = IO.getIndex ()
                        return IO.getLocalNodes (), Some(nodes |> Array.map (fun ver -> ver.version))
                    | true, false ->
                        AnsiConsole.MarkupLine "[yellow]Checking local versions[/]"

                        let! nodes = IO.getIndex ()
                        return IO.getLocalNodes (), Some(nodes |> Array.map (fun ver -> ver.version))
                    | false, true ->
                        AnsiConsole.MarkupLine
                            "[yellow]Warning[/]: --update can only be used when [yellow]--remote true[/] is set, ignoring."

                        return IO.getLocalNodes (), None
                    | _ -> return IO.getLocalNodes (), None
                }

            let table = getVersionsTable local remote
            AnsiConsole.Render table
            return 0
        }
