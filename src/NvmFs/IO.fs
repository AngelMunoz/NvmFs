namespace NvmFs

open System
open ICSharpCode.SharpZipLib.Tar
open System.IO
open System.IO.Compression
open System.Runtime.InteropServices
open System.Security.Cryptography
open System.Threading.Tasks


open CliWrap
open CliWrap.Buffered

open Thoth.Json.Net

open Spectre.Console

open NvmFs

module IO =
    open System.Formats.Tar
    open System.Diagnostics

    let createSymlink (actualPath: string) (symbolicLink: string) =
        try
            File.CreateSymbolicLink(symbolicLink, actualPath) |> Ok
        with ex ->
            // for more information please see https://github.com/dotnet/runtime/issues/24271
            Error $"Could not write symlink {symbolicLink} -> {actualPath}\n {ex.Message}"

    let removeSymlink (path: string) =
        try
            File.Delete(path)
            Ok()
        with
        | :? DirectoryNotFoundException as ex ->
            Debug.WriteLine("Symlink is not present", ex)
            Ok()
        | :? UnauthorizedAccessException
        | :? IOException as ex -> Error $"Failed to delete symlink: {path} - [red]{ex.Message}[/]"

    let trySetPermissionsUnix (dir: FileSystemInfo) =
        try
            dir.UnixFileMode <- UnixFileMode.UserExecute
            Ok()
        with
        | :? UnauthorizedAccessException as ex -> Error(PermissionError ex.Message)
        | :? IOException as ex -> Error(SymlinkError ex.Message)

    let extractFromStream (os: CurrentOS, output: string, source: Stream) =

        try
            DirectoryInfo(output).Create()

            match os with
            | Windows ->
                use zip = new ZipArchive(source)
                zip.ExtractToDirectory(output)
            | _ ->
                use outer = new GZipStream(source, CompressionMode.Decompress)
                TarFile.ExtractToDirectory(outer, output, true)
        with :? IOException as ex ->
            if
                ex.Message.Contains("already exists.")
                || ex.InnerException.Message.Contains("already exists")
            then
                ()
            else
                reraise ()

    let createHomeDir () =
        let dir = Common.getHome ()

        Directory.CreateDirectory(dir)

    let getParentDir (file: string) =
        let file = FileInfo(file)
        file.DirectoryName

    let getCurrentNodeVersion (os: CurrentOS) =
        task {
            let cmd =
                match os with
                | Windows -> Cli.Wrap("node.exe")
                | _ -> Cli.Wrap("node")

            let! result =
                cmd
                    .WithArguments("--version")
                    .WithValidation(CommandResultValidation.ZeroExitCode)
                    .ExecuteBufferedAsync()
                    .Task

            return result.StandardOutput
        }

    let removeHomeDir () =
        let dir = Common.getHome ()
        Directory.Delete(dir, true)

    let SymLinkTarget = Path.Combine(Common.getHome (), "current")

    let versionDirectory (version: string) =
        Path.Combine(Common.getHome (), version, "bin")


    let getIndex () =
        let path = Common.getHome ()

        let content =
            try
                File.ReadAllText(Path.Combine(path, "index.json"))
            with _ ->
                String.Empty

        match Decode.fromString (Decode.array NodeVerItem.Decoder) content with
        | Ok res -> res
        | Error err -> [||]

    let removeExtension (file: string) =
        let file = FileInfo(file)
        file.FullName.Replace(file.Extension, String.Empty)

    let isMarkerInBashrc path =
        let bashrc = File.ReadAllText path

        bashrc.Contains(Common.StartMarker, StringComparison.InvariantCultureIgnoreCase)
        || bashrc.Contains(Common.EndMarker, StringComparison.InvariantCultureIgnoreCase)

    let tryUpdateBashrc (lines: string list) =
        let path =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), ".bashrc")

        try

            if isMarkerInBashrc path then
                ()
            else
                File.AppendAllLines(path, lines)

            Ok()
        with :? IOException as ex ->
            Error $"Failed to append patht to [yellow]{path}[/]"


    let codenameExistsInDisk (codename: string) =
        let home = DirectoryInfo(Common.getHome ())

        home.GetDirectories() |> Array.exists (fun dir -> dir.Name.Contains(codename))

    let versionExistsInDisk (codename: string) (version: string) =
        let home = DirectoryInfo(Common.getHome ())
        let directories = home.GetDirectories()

        let exists = directories |> Array.exists (fun dir -> dir.Name.Contains(codename))

        if not exists then
            false
        else
            directories
            |> Array.map (fun dir -> dir.GetDirectories())
            |> Array.reduce Array.append
            |> Array.map (fun dir -> dir.Name.Split('-').[1])
            |> Array.exists (fun dir -> dir.Contains(version))


    let getLocalNodes () =
        let home = DirectoryInfo(Common.getHome ())

        home.GetDirectories()
        |> Array.filter (fun dir -> not (dir.FullName.Contains("bin")))
        |> Array.map (fun dir -> dir.Name)
        |> Array.sortDescending
