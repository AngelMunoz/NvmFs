namespace NvmFs

open FSharp.Control.Tasks
open CommandLine
open NvmFs.Cmd
open Spectre.Console

module Main =

    [<EntryPoint>]
    let main argv =

        let result = Parser.Default.ParseArguments<Install, Use, List, Uninstall>(argv)

        let result =
            task {
                match result with
                | :? (Parsed<obj>) as cmd ->
                    match cmd.Value with
                    | :? Install as opts -> return! Actions.Install opts
                    | :? Use as opts -> return! Actions.Use opts
                    | :? Uninstall as opts -> return! Actions.Uninstall opts
                    | :? List as opts -> return! Actions.List opts
                    | _ -> return 1
                | _ -> return 1
            }
            |> Async.AwaitTask
            |> Async.Catch
            |> Async.RunSynchronously

        match result with
        | Choice1Of2 result -> result
        | Choice2Of2 ex ->
            AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything)
            1
