module NvmFs.Main

open FSharp.SystemCommandLine
open Input
open NvmFs.Cmd

let installCommand = command "install" {
  description "Installs the specified node version or the latest LTS by default"

  inputs(
    argumentMaybe "version" |> desc "Installs the specified node version",
    option "--lts"
    |> alias "-l"
    |> desc "Ignores version and pulls down the latest LTS version",
    option "--current"
    |> alias "-c"
    |> desc "Ignores version and pulls down the latest Current version",
    option "--default"
    |> alias "-d"
    |> def false
    |> desc "Sets the downloaded version as default"
  )

  setAction Actions.Install
}

let uninstallCommand = command "uninstall" {
  description "Uninstalls the specified node version"

  inputs(
    argumentMaybe "version" |> desc "Uninstalls the specified node version"
  )

  setAction Actions.Uninstall
}

let useCommand = command "use" {
  description "Sets the Node Version"

  inputs(
    argumentMaybe "version" |> desc "Sets the specified node version",
    option "--lts"
    |> alias "-l"
    |> desc "Ignores version and pulls down the latest LTS version",
    option "--current"
    |> alias "-c"
    |> desc "Ignores version and pulls down the latest Current version"
  )

  setAction Actions.Use
}

let listCommand = command "list" {
  description "Shows the available node versions"

  inputs(
    optionMaybe "--remote"
    |> alias "-r"
    |> desc "Displays the last downloaded version index in the console",
    optionMaybe "--update"
    |> alias "-u"
    |> desc
      "Use together with --remote, pulls the version index from the node website"
  )

  setAction Actions.List
}

[<EntryPoint>]
let main argv =
  rootCommand argv {
    description
      "nvmfs is a simple node version manager that just downloads and sets node versions. That's it!"

    inputs context
    helpActionAsync
    addCommand installCommand
    addCommand uninstallCommand
    addCommand useCommand
    addCommand listCommand
  }
  |> Async.AwaitTask
  |> Async.RunSynchronously
