namespace NvmFs

open System
open Spectre.Console

module Env =

    let setNvmFsNodeWin (home: string) =
        Environment.SetEnvironmentVariable
            (Common.EnvVars.NvmFsNode, (IO.fullPath (home, [ "bin/" ])), EnvironmentVariableTarget.User)

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
                IO.deleteDirs node
            with ex ->
                AnsiConsole.WriteLine
                    $"Failed to Delete: {node} it's very likely that the directory didn't exist before"
#if DEBUG
                AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything)
#endif

            IO.createSymlink versionBin (IO.fullPath (home, []))
        | None ->
            match os with
            | "win" -> setNvmFsNodeWin home
            | _ -> setNvmFsNodeUnix home

            IO.createSymlink versionBin (IO.fullPath (home, []))
