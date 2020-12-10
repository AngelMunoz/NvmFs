namespace NvmFs

open System
open System.IO
open System.IO.Compression
open System.Runtime.InteropServices
open System.Security.Cryptography
open SharpCompress.Common
open SharpCompress.Readers
open CliWrap
open CliWrap.Buffered
open Thoth.Json.Net
open FSharp.Control.Tasks
open Spectre.Console

module IO =
    let createSymlink (symbolicLink: string) (actualPath: string) =
        if (RuntimeInformation.IsOSPlatform OSPlatform.Linux
            || RuntimeInformation.IsOSPlatform OSPlatform.OSX) then
            let symbolicLinkDirectory = Path.GetDirectoryName(symbolicLink)

            if not (Directory.Exists(symbolicLinkDirectory)) then
                Directory.CreateDirectory(symbolicLinkDirectory)
                |> ignore

            let result =
                Cli
                    .Wrap("ln")
                    .WithArguments($"-s {actualPath} {symbolicLink}")
                    .WithValidation(CommandResultValidation.None)
                    .ExecuteBufferedAsync()
                    .Task
                |> Async.AwaitTask
                |> Async.RunSynchronously

            if result.ExitCode <> 0
            then AnsiConsole.MarkupLine
                     $"[#d78700]Error while creating symlinks[/]:\n[#ff8700]{result.StandardError}[/]"

            ()
        else

        if (RuntimeInformation.IsOSPlatform OSPlatform.Windows) then
            let symbolicLinkDirectory = Path.GetDirectoryName(symbolicLink)

            if not (Directory.Exists(symbolicLinkDirectory)) then
                Directory.CreateDirectory(symbolicLinkDirectory)
                |> ignore
            /// **NOTE**: This requires DevMode enabled on Windows10
            let result =
                Cli
                    .Wrap("New-Item")
                    .WithArguments($"-Path {actualPath} -ItemType SymbolicLink -Value {symbolicLink}")
                    .WithValidation(CommandResultValidation.None)
                    .ExecuteBufferedAsync()
                    .Task
                |> Async.AwaitTask
                |> Async.RunSynchronously

            if result.ExitCode <> 0
            then eprintfn "Error while creating symlinks:\n%s" result.StandardError

            ()
        else
            printfn
                $"Could not write symlink {symbolicLink} -> {actualPath}, for more information please see https://github.com/dotnet/runtime/issues/24271"

    let extractContents (os: string) (source: string) (output: string) =
        match os with
        | "win" -> ZipFile.ExtractToDirectory(source, output)
        | _ ->
            let sourceFile = FileInfo(source)
            use sourceStr = sourceFile.OpenRead()
            use reader = ReaderFactory.Open(sourceStr)
            let opts = ExtractionOptions()
            opts.ExtractFullPath <- true
            opts.Overwrite <- true
            opts.WriteSymbolicLink <- new ExtractionOptions.SymbolicLinkWriterDelegate(createSymlink)
            reader.WriteAllToDirectory(output, opts)

    let getChecksumForFile (file: string) =
        use sha256 = SHA256.Create()
        use str = File.OpenRead(Path.GetFullPath(file))
        let hash = sha256.ComputeHash str

        BitConverter
            .ToString(hash)
            .Replace("-", String.Empty)
            .ToLowerInvariant()

    let verifyChecksum (file: string) (against: string) =
        let checksum = getChecksumForFile file
        checksum = against

    let openStreamForPath (path: string) =
        let file = FileInfo(path)
        file.Directory.Create()
        file.OpenWrite()

    let createHomeDir () =
        let dir = NvmFs.Common.getHome ()

        Directory.CreateDirectory(dir)

    let createTmpDir () =
        let dir = NvmFs.Common.getDownloadTmpDir ()

        Directory.CreateDirectory(dir)

    let rec private deleteDirs (path: string) =
        let dir = DirectoryInfo(path)
        let dirs = dir.EnumerateDirectories(path)

        if Seq.isEmpty dirs then
            Directory.Delete path
        else
            for directory in dirs do
                deleteDirs directory.FullName

    let removeHomeDir () =
        let dir = NvmFs.Common.getHome ()
        deleteDirs dir

    let removeTmpDir () =
        let dir = NvmFs.Common.getDownloadTmpDir ()
        deleteDirs dir

    let fullPath (path: string, paths: string list) =
        let paths = path :: paths
        Path.GetFullPath(Path.Combine(paths |> Array.ofList))


    let getIndex () =
        task {
            let path = Common.getHome ()

            use file =
                File.OpenText(fullPath (path, [ "index.json" ]))

            let! content = file.ReadToEndAsync()

            return
                match Decode.fromString (Decode.array NodeVerItem.Decoder) content with
                | Ok res -> res
                | Error err -> [||]
        }

    let removeExtension (file: string) =
        let file = FileInfo(file)
        file.FullName.Substring(0, file.FullName.Length - file.Extension.Length)

    let getChecksumForVersion (checksumfile: string) (version: string) (os: string) (arch: string) =
        let line =
            let ext =
                match os with
                | "win" -> ".zip"
                | _ -> "tar.gz"

            $"node-{version}-{os}-{arch}.{ext}"

        let file = FileInfo(checksumfile)
        use file = file.OpenText()
        let content = file.ReadToEnd()
        let lines = content.Split('\n')

        lines
        |> Array.tryFind (fun l -> l.Contains(line))
        // if we find the line get the checksum which should be the first item
        |> Option.map (fun l -> l.Split(' ') |> Array.tryHead)
        |> Option.flatten
