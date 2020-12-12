namespace NvmFs

open System
open Spectre.Console
open System.Collections
open System.Linq

module Env =

    let setNvmFsNodeWin (home: string) =
        let nvmfsnode = (IO.fullPath (home, [ "bin/" ]))
        Environment.SetEnvironmentVariable(Common.EnvVars.NvmFsNode, nvmfsnode, EnvironmentVariableTarget.User)

        let path =
            let mapped =
                Environment
                    .GetEnvironmentVariables(EnvironmentVariableTarget.User)
                    .Cast<DictionaryEntry>()
                |> Seq.map (fun de -> string de.Key, string de.Value)
                |> Map.ofSeq

            let values = (mapped.Item "Path").Split(';')

            values
            |> Array.map
                (fun pathValue ->
                    if not
                        (mapped
                         |> Map.exists (fun _ value -> value = pathValue)) then
                        pathValue
                    else
                        let value =
                            mapped
                            |> Seq.tryFind (fun entry -> entry.Value = pathValue)

                        match value with
                        | Some kvp -> $"%%{kvp.Key}%%"
                        | None -> pathValue

                    )
            |> Array.reduce (fun curr next -> $"{curr};{next}")

        Environment.SetEnvironmentVariable
            ("PATH", $"%%{Common.EnvVars.NvmFsNode}%%;{path}", EnvironmentVariableTarget.User)

    let setNvmFsNodeUnix (home: string) =
        let nodepath = IO.fullPath (home, [ "bin/" ])

        let lines =
            [ ""
              $"export %s{Common.EnvVars.NvmFsNode}={nodepath}"
              $"export PATH=$%s{Common.EnvVars.NvmFsNode}:$PATH" ]

        IO.appendToBashRc (lines)


    let setEnvVersion (os: string) (versionBin: string) =
        let home = Common.getHome ()

        let nvmfsnode =
            Environment.GetEnvironmentVariable(Common.EnvVars.NvmFsNode)
            |> Option.ofObj

        match nvmfsnode with
        | Some node ->
            try
                match os with
                | "win" ->
                    let result = IO.deleteSymlink node

                    if result.ExitCode <> 0
                    then AnsiConsole.WriteLine($"Failed to delete symlink: {node} - [red]{result.StandardError}[/]")
                | _ -> IO.deleteFile node
            with ex ->
                AnsiConsole.WriteLine
                    $"Failed to Delete: {node} it's very likely that the directory didn't exist before"
#if DEBUG
                AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything)
#endif

            IO.createSymlink versionBin (IO.fullPath (home, [ if os = "win" then "bin" ]))
        | None ->
            match os with
            | "win" -> setNvmFsNodeWin home
            | _ -> setNvmFsNodeUnix home

            IO.createSymlink versionBin (IO.fullPath (home, [ if os = "win" then "bin" ]))
