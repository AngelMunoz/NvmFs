namespace NvmFs

open System
open Spectre.Console
open System.Collections
open System.Linq

module Env =

    let setNvmFsNodeWin (home: string) =
        let isEnvThere =
            Environment.GetEnvironmentVariable(Common.EnvVars.NvmFsNode, EnvironmentVariableTarget.User)
            |> Option.ofObj

        match isEnvThere with
        | Some value ->
            let path =
                TextPath(path = value)
                    .SeparatorColor(Color.Yellow)
                    .RootColor(Color.Yellow)

            AnsiConsole.Markup $"[bold yellow]%%{Common.EnvVars.NvmFsNode}%%[/] is already set to: "
            AnsiConsole.Write path
            AnsiConsole.MarkupLine " nothing to do here."

        | None ->
            let nvmfshome = (IO.fullPath (home, []))
            let nvmfsnode = (IO.fullPath (home, [ "bin/" ]))
            let varHome = $"%%{Common.EnvVars.NvmFsHome}%%"
            let varNode = $"%%{Common.EnvVars.NvmFsNode}%%"

            AnsiConsole.MarkupLineInterpolated
                $"Adding [bold yellow]{varHome}[/], [bold yellow]{varNode}[/] to the user env variables."

            Environment.SetEnvironmentVariable(Common.EnvVars.NvmFsNode, nvmfsnode, EnvironmentVariableTarget.User)
            Environment.SetEnvironmentVariable(Common.EnvVars.NvmFsHome, nvmfshome, EnvironmentVariableTarget.User)

            AnsiConsole.MarkupLine "In order to avoid re-writing your environment variables in [bold red]%PATH%[/]"

            AnsiConsole.MarkupLineInterpolated
                $"You will have to add [bold yellow]{varHome}[/], [bold yellow]{varNode}[/] to the [bold red]%%PATH%%[/] yourself"

            AnsiConsole.Markup "For that you can run [bold red]SystemPropertiesAdvanced.exe[/] "
            AnsiConsole.MarkupLine "click on the \"Environment Variables...\" button and add it to you user's PATH"

            AnsiConsole.MarkupLineInterpolated
                $"ex. C:\\DirA\\bin;C:\\dir b\\tools\\bin;[bold yellow]%%{Common.EnvVars.NvmFsNode}%%\\bin[/];"

            AnsiConsole.MarkupLine
                "Once you have done that, you will need to log out and log in (or restart)\nto let your system load the environment"



    let setNvmFsNodeUnix (home: string) =
        let nodepath = IO.fullPath (home, [ "bin/" ])

        let lines =
            [ ""
              $"export %s{Common.EnvVars.NvmFsNode}={nodepath}"
              $"export PATH=$%s{Common.EnvVars.NvmFsNode}:$PATH" ]

        IO.appendToBashRc (lines)


    let setEnvVersion (os: CurrentOS) (versionBin: string) =
        let home = Common.getHome ()

        let nvmfsnode =
            Environment.GetEnvironmentVariable(Common.EnvVars.NvmFsNode, EnvironmentVariableTarget.User)
            |> Option.ofObj

        match nvmfsnode with
        | Some node ->
            let result = IO.deleteSymlink node os

            if result.ExitCode <> 0 then
                AnsiConsole.MarkupLineInterpolated($"Failed to delete symlink: {node} - [red]{result.StandardError}[/]")

            IO.createSymlink versionBin (IO.fullPath (home, [ if os = Windows then "bin" ]))
        | None ->
            match os with
            | Windows -> setNvmFsNodeWin home
            | _ -> setNvmFsNodeUnix home

            IO.createSymlink versionBin (IO.fullPath (home, [ if os = Windows then "bin" ]))
