namespace NvmFs.Cmd

open System
open System.Threading.Tasks
open Spectre.Console
open FsToolkit.ErrorHandling
open NvmFs

[<RequireQualifiedAccess>]
module Actions =
    let validateVersion (num: string) =
        if num.IndexOf('v') = 0 then
            let (parsed, _) = num.Substring 1 |> System.Int32.TryParse
            parsed
        else
            let (parsed, _) = System.Int32.TryParse(num)
            parsed

    let getInstallType (isLts: bool option) (isCurrent: bool option) (version: string option) : Result<InstallType, string> =

        match isLts, isCurrent, version with
        | Some lts, None, None ->
            if lts then
                Ok LTS
            else
                Result.Error "No valid version was presented"
        | None, Some current, None ->
            if current then
                Ok Current
            else
                Result.Error "No valid version was presented"
        | None, None, Some version ->
            match version.Split(".") with
            | [| major |] ->
                if validateVersion major then
                    Ok(SpecificM major)
                else
                    Result.Error $"{version} is not a valid node version"
            | [| major; minor |] ->
                if validateVersion major && validateVersion minor then
                    Ok(SpecificMM(major, minor))
                else
                    Result.Error $"{version} is not a valid node version"
            | [| major; minor; patch |] ->
                if validateVersion major
                   && validateVersion minor
                   && validateVersion patch then
                    Ok(SpecificMMP(major, minor, patch))
                else
                    Result.Error $"{version} is not a valid node version"
            | _ -> Result.Error $"{version} is not a valid node version"
        | _ -> Result.Error $"Use only one of --lts 'boolean', --current 'boolean', or --version 'string'"

    let setVersionAsDefault (version: string) (codename: string) (os: CurrentOS) (arch: string) =
        taskResult {
            let directory = Common.getVersionDirName version os arch

            let symlinkpath = IO.getSymlinkPath codename directory os

            do!
                Env.setEnvVersion os symlinkpath
                |> Result.mapError SymlinkError

            match os with
            | Windows -> ()
            | Mac
            | Linux ->
                AnsiConsole.MarkupLine("[yellow]Setting permissions for node[/]")

                do!
                    IO.trySetPermissionsUnix symlinkpath
                    |> TaskResult.mapError (fun errors ->
                        errors
                        |> List.fold (fun value next -> $"{value}\n{next}") ""
                        |> PermissionError)
            | FreeBSD -> return! Result.Error UnsuppoertdOS
        }

    let runPreInstallChecks () =
        let homedir = IO.createHomeDir ()
        AnsiConsole.MarkupLine("[bold yellow]Updating node versions[/]")

        task {
            let! file = Network.downloadNodeVersions (homedir.FullName)
            AnsiConsole.MarkupLine($"[green]Updated node versions on {file}[/]")
        }
        :> Task

    let tryCleanAfterDownload (checksums: string) (node: string) =
        try
            let dirname = IO.getParentDir checksums
            IO.deleteFile node
            IO.deleteFile checksums
            IO.deleteDir dirname
            Ok()
        with
        | ex -> Result.Error ex

    let downloadNodeAndChecksum (version: NodeVerItem) (setDefault: bool) =
        taskResult {
            let! codename, os, arch =
                Common.getOsArchCodename version
                |> Result.setError PlatformError

            let! checksums = Network.downloadChecksumsForVersion $"{version.version}"
            let! node = Network.downloadNode $"{version.version}" version.version os arch

            AnsiConsole.MarkupLine $"[#5f5f00]Downloaded[/]: {checksums} - {node}"

            let! checksum =
                IO.getChecksumForVersion checksums version.version os arch
                |> Result.requireSome ChecksumNotFound

            do!
                IO.verifyChecksum node checksum
                |> Result.requireTrue ChecksumMissmatch
                |> Result.teeError (fun _ ->
                    AnsiConsole.MarkupLineInterpolated
                        $"[bold red]The Checksums didnt match\ndownload: {IO.getChecksumForFile node}\nchecksum: None[/]")

            let what = $"[yellow]{node}[/]"
            let target = $"[yellow]{Common.getHome ()}/latest-{codename}[/]"

            AnsiConsole.MarkupLine $"[#5f5f00]Extracting[/]: {what} to {target}"

            IO.extractContents os node (IO.fullPath (Common.getHome (), [ $"latest-{codename}" ]))
            AnsiConsole.MarkupLine "[green]Extraction Complete![/]"

            if setDefault then
                do!
                    setVersionAsDefault version.version codename os arch
                    |> TaskResult.mapError (fun err -> FailedToSetDefault err.Value)

            return (checksums, node)
        }

    let validateVersionGroup (version: string option, lts: bool option, current: bool option) = 
        if [version.IsSome; lts.IsSome; current.IsSome] |> List.filter id |> List.length > 1 
        then failwith "Can only have one of 'version', '--lts' or '--current'."

    let Install (version: string option, lts: bool option, current: bool option, isDefault: bool) =
        task {
            validateVersionGroup (version, lts, current)
            do! runPreInstallChecks ()

            let! versions = IO.getIndex ()

            match getInstallType lts current version, isDefault with
            | Ok install, setAsDefault ->
                let version = Common.getVersionItem versions install

                match version with
                | None ->
                    AnsiConsole.MarkupLine "[bold red]Version Not found[/]"
                    return 1
                | Some version ->
                    let! downloadRes = downloadNodeAndChecksum version setAsDefault

                    match downloadRes with
                    | Ok (checksums, node) ->
                        AnsiConsole.MarkupLine $"[bold green]Node version {version.version} installed[/]"

                        if setAsDefault then
                            AnsiConsole.MarkupLine $"[bold green]Set {version.version} as default[/]"

                        match tryCleanAfterDownload checksums node with
                        | Ok _ -> ()
                        | Error ex ->
#if DEBUG
                            AnsiConsole.WriteException(ex, ExceptionFormats.ShortenPaths)
#endif
                            AnsiConsole.MarkupLineInterpolated $"[yellow]We could not clean the download ${ex}[/]"

                        return 0
                    | Error (FailedToSetDefault msg) ->
                        AnsiConsole.MarkupLine $"[red]We could not set {version.version} as default: {msg}[/]"

                        return 1
                    | Error PlatformError ->
                        AnsiConsole.MarkupLine
                            $"[bold red]We were unable to get the current platform and architecture[/]"

                        return 1
                    | Error ChecksumNotFound
                    | Error ChecksumMissmatch -> return 1
            | Result.Error err, _ ->
                AnsiConsole.MarkupLine $"[bold red]{err}[/]"
                return 1
        }

    let findAndSetVersion version =
        taskResult {
            let! codename, os, arch =
                Common.getOsArchCodename version
                |> Result.setError UseError.PlatformError

            do!
                IO.codenameExistsInDisk codename
                |> Result.requireTrue (UseError.NodeNotInDisk codename)

            AnsiConsole.MarkupLine $"[bold yellow]Setting version[/] [green]%s{version.version}[/]"

            return!
                setVersionAsDefault version.version codename os arch
                |> TaskResult.mapError (fun err -> UseError.FailedToSetDefault err.Value)
        }

    let Use (version: string option, lts: bool option, current: bool option) =
        task {            
            validateVersionGroup (version, lts, current)
            AnsiConsole.MarkupLine $"[bold yellow]Checking local versions[/]"

            let! versions = IO.getIndex ()

            match getInstallType lts current version with
            | Ok install ->
                let version = Common.getVersionItem versions install

                match version with
                | Some version ->

                    let! findAndSetResult = findAndSetVersion version

                    match findAndSetResult with
                    | Error UseError.PlatformError ->
                        AnsiConsole.MarkupLine
                            $"[bold red]We were unable to get the current platform and architecture[/]"

                        return 1
                    | Error (UseError.FailedToSetDefault msg) ->
                        AnsiConsole.MarkupInterpolated
                            $"[yellow]We could not set {version.version} as default: ${msg}[/]"

                        return 1
                    | Error (UseError.NodeNotInDisk codename) ->
                        let l1 = "[bold red]We didn't find version[/]"
                        let l2 = $"[bold yellow]%s{version.version}[/]"
                        AnsiConsole.MarkupLine $"{l1} {l2} within [bold yellow]%s{codename}[/]"
                        return 1
                    | Ok _ ->
                        AnsiConsole.MarkupLine $"[bold green]Node version {version.version} set as the default[/]"
                        return 0
                | None ->
                    AnsiConsole.MarkupLine "[bold red]Version Not found[/]"
                    return 1
            | Error err ->
                AnsiConsole.MarkupLine $"[bold red]{err}[/]"
                return 1
        }

    let doUninstall version =
        result {
            let! codename, os, arch =
                Common.getOsArchCodename version
                |> Result.setError UninstallError.PlatformError

            do!
                IO.codenameExistsInDisk codename
                |> Result.requireTrue UninstallError.NodeNotInDisk

            AnsiConsole.MarkupLine $"[yellow]Uninstalling version[/]: %s{version.version}"

            let path =
                IO.fullPath (
                    Common.getHome (),
                    [ $"latest-{codename}"
                      Common.getVersionDirName version.version os arch ]
                )

            AnsiConsole.MarkupLine $"[yellow]Uninstalling version[/]: %s{version.version}"

            try
                IO.deleteDir path
                return ()
            with
            | ex -> return! UninstallError.FailedToDelete ex |> Result.Error
        }

    let Uninstall (version: string option) =
        task {
            let! versions = IO.getIndex ()

            match getInstallType None None version with
            | Ok install ->
                let version = Common.getVersionItem versions install

                match version with
                | Some version ->
                    match doUninstall version with
                    | Ok _ ->
                        AnsiConsole.MarkupLine
                            $"[green]Uninstalled Version[/] [bold yellow]%s{version.version}[/][green] successfully[/]"

                        return 0
                    | Error UninstallError.PlatformError ->
                        AnsiConsole.MarkupLine
                            $"[bold red]We were unable to get the current platform and architecture[/]"

                        return 1
                    | Error UninstallError.NodeNotInDisk ->
                        let l1 = $"[red]Version[/] [bold yellow]%s{version.version}[/]"

                        let l2 = $"[red]is not present in the system, aborting.[/]"

                        AnsiConsole.MarkupLine $"{l1} {l2}"
                        return 1

                    | Error (UninstallError.FailedToDelete ex) ->
                        AnsiConsole.MarkupLine $"[bold red]Failed to delete {version.version}[/]"
#if DEBUG
                        AnsiConsole.WriteException(ex, ExceptionFormats.ShortenPaths)
#endif
                        return 1
                | None ->
                    AnsiConsole.MarkupLine "[bold red]Version Not found[/]"
                    return 1
            | Result.Error err ->
                AnsiConsole.MarkupLine $"[bold red]{err}[/]"
                return 1
        }

    let List (remote: bool option, updateIndex: bool option) =
        task {
            let checkRemote = remote |> Option.defaultValue false
            let updateIndex = checkRemote && (updateIndex |> Option.defaultValue false)

            let! currentVersion =
                task {
                    match Common.getOSPlatform () with
                    | Ok os ->
                        let! result = IO.getCurrentNodeVersion os

                        if result.ExitCode <> 0 then
                            return ""
                        else
                            return result.StandardOutput.Trim()
                    | Error err -> return ""
                }

            let getVersionsTable (localVersions: string []) (remoteVersions: string [] option) =
                let remoteVersions = defaultArg remoteVersions [||]

                let table =
                    Table()
                        .AddColumns(
                            [| TableColumn("Local")
                               if remoteVersions.Length > 0 then
                                   TableColumn("Remote") |]
                        )

                table.Title <- TableTitle("Node Versions\n[green]* currently set as default[/]")

                let longestLength =
                    if localVersions.Length > remoteVersions.Length then
                        localVersions.Length
                    else
                        remoteVersions.Length

                let markCurrent (version: string) =
                    if version.Contains(currentVersion) then
                        $"[green]{version}*[/]"
                    else
                        version

                for i in 0 .. longestLength - 1 do
                    let localVersion =
                        localVersions
                        |> Array.tryItem i
                        |> Option.defaultValue ""

                    table.AddRow(
                        [| markCurrent localVersion
                           if remoteVersions.Length > 0 then
                               markCurrent (
                                   remoteVersions
                                   |> Array.tryItem i
                                   |> Option.defaultValue ""
                               ) |]
                    )
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
            AnsiConsole.Write table
            return 0
        }
