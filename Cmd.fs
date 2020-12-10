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

[<Verb("uninstall", HelpText = "Uninstalls the specified node version or the latest Current by default")>]
type Uninstall =
    { [<Option('v', "version", Group = "version", HelpText = "Removes the specified node version")>]
      version: string
      [<Option('l', "lts", Group = "version", HelpText = "Ignores version and removes the latest LTS version")>]
      lts: Nullable<bool>
      [<Option('c', "current", Group = "version", HelpText = "Ignores version and removes latest Current version")>]
      current: Nullable<bool> }

[<Verb("use", HelpText = "Sets the Node Version")>]
type Use =
    { [<Option('v', "version", Group = "version", HelpText = "sets the specified node version in the PATH")>]
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

[<Verb("list", HelpText = "Sets the Node Version")>]
type List =
    { [<Option('r', "remote", Required = false, HelpText = "Pulls the version list from the node website")>]
      remote: Nullable<bool> }

[<RequireQualifiedAccess>]
module Actions =
    let private validateVersion (num: string) =
        if num.IndexOf('v') = 0 then
            let (parsed, _) = num.Substring 1 |> System.Int32.TryParse
            parsed
        else
            let (parsed, _) = System.Int32.TryParse(num)
            parsed

    let private getInstallType (options: Install): Result<InstallType, string> =
        let isLts = options.lts |> Option.ofNullable
        let isCurrent = options.current |> Option.ofNullable
        let version = options.version |> Option.ofObj

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
        | _ ->
            Result.Error
                $"Use only one of --{nameof options.lts}, --{nameof options.current}, or --{nameof options.version}"


    let private getVersionItem (versions: NodeVerItem []) (install: InstallType) =
        match install with
        | LTS ->
            versions
            |> Array.choose (fun version -> if version.lts.IsSome then Some version else None)
            |> Array.tryHead
        | Current -> versions |> Array.tryHead
        | SpecificM major ->
            let major =
                if major.ToLowerInvariant().StartsWith('v') then major else $"v{major}"

            versions
            |> Array.tryFind (fun ver -> ver.version.StartsWith($"{major}."))
        | SpecificMM (major, minor) ->
            let major =
                if major.ToLowerInvariant().StartsWith('v') then major else $"v{major}"

            versions
            |> Array.tryFind (fun ver -> ver.version.StartsWith($"{major}.{minor}."))
        | SpecificMMP (major, minor, patch) ->
            let major =
                if major.ToLowerInvariant().StartsWith('v') then major else $"v{major}"

            versions
            |> Array.tryFind (fun ver -> ver.version.Contains($"{major}.{minor}.{patch}"))

    let private runPreChecks () =
        let homedir = IO.createHomeDir ()
        AnsiConsole.MarkupLine("[bold yellow]Updating node versions[/]")

        task {
            let! file = Network.downloadNodeVersions (homedir.FullName)
            AnsiConsole.MarkupLine($"[green]Updated node versions to {file}[/]")
        }
        :> Task


    let Install (options: Install) =
        task {
            do! runPreChecks ()

            let! versions = IO.getIndex ()

            match getInstallType options,
                  (Option.ofNullable options.isDefault
                   |> Option.defaultValue false) with
            | Ok install, setAsDefault ->
                let version = getVersionItem versions install

                match version with
                | Some version ->
                    let os = Common.getPlatform ()
                    let arch = Common.getArch ()

                    let codename =
                        let defVersion =
                            Common.getVersionCodename version.version

                        let defLts =
                            version.lts |> Option.map Common.getLtsCodename

                        defaultArg defLts (defVersion)

                    let! checksums = Network.downloadChecksumsForVersion $"latest-{codename}"
                    let! node = Network.downloadNode $"latest-{codename}" version.version os arch
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

                            IO.extractContents
                                os
                                node
                                (IO.removeExtension (IO.fullPath (Common.getHome (), [ $"latest-{codename}" ])))

                            AnsiConsole.MarkupLine "[green]Extraction Complete![/]"
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


    let Use (options: Use) = 0
    let Uninstall (options: Uninstall) = 0
    let List (options: List) = 0
