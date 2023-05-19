module NvmFs.Main

open FSharp.SystemCommandLine
open System.Threading.Tasks
open NvmFs.Cmd
open System.CommandLine.Invocation
open System.CommandLine.Help

let installCommand =
    command "install" {
        description "Installs the specified node version or the latest LTS by default"

        inputs (
            Input.ArgumentMaybe<string>("version", "Installs the specified node version"),
            Input.OptionMaybe<bool>([ "-l"; "--lts" ], "Ignores version and pulls down the latest LTS version"),
            Input.OptionMaybe<bool>([ "-c"; "--current" ], "Ignores version and pulls down the latest Current version"),
            Input.Option<bool>([ "-d"; "--default" ], false, "Sets the downloaded version as default")
        )

        setHandler Actions.Install
    }

let uninstallCommand =
    command "uninstall" {
        description "Uninstalls the specified node version"
        inputs (Input.ArgumentMaybe<string>("version", "Installs the specified node version"))
        setHandler Actions.Uninstall
    }

let useCommand =
    command "use" {
        description "Sets the Node Version"

        inputs (
            Input.ArgumentMaybe<string>("version", "Installs the specified node version"),
            Input.OptionMaybe<bool>([ "-l"; "--lts" ], "Ignores version and pulls down the latest LTS version"),
            Input.OptionMaybe<bool>([ "-c"; "--current" ], "Ignores version and pulls down the latest Current version")
        )

        setHandler Actions.Use
    }

let listCommand =
    command "list" {
        description "Shows the available node versions"

        inputs (
            Input.OptionMaybe<bool>([ "-r"; "--remote" ], "Displays the last downloaded version index in the console"),
            Input.OptionMaybe<bool>(
                [ "-u"; "--update" ],
                "Use together with --remote, pulls the version index from the node website"
            )
        )

        setHandler Actions.List
    }

let rootHandler (ctx: InvocationContext) =
    task {
        let hc =
            HelpContext(ctx.HelpBuilder, ctx.Parser.Configuration.RootCommand, System.Console.Out)

        ctx.HelpBuilder.Write(hc)

        return 0
    }

[<EntryPoint>]
let main argv =
    rootCommand argv {
        description "nvmfs is a simple node version manager that just downloads and sets node versions. That's it!"
        inputs (Input.Context())
        setHandler rootHandler
        addCommand installCommand
        addCommand uninstallCommand
        addCommand useCommand
        addCommand listCommand
    }
    |> Async.AwaitTask
    |> Async.RunSynchronously
