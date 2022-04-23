namespace NvmFs

open System
open System.IO
open System.IO.Compression
open System.Runtime.InteropServices
open System.Security.Cryptography
open NvmFs
open SharpCompress.Common
open SharpCompress.Readers
open CliWrap
open CliWrap.Buffered
open Thoth.Json.Net
open Spectre.Console
open System.Threading.Tasks


module IO =
    let createSymlink (actualPath: string) (symbolicLink: string) =
        let os = Common.getOSPlatform ()

        match os with
        | Common.IsMacOrLinux ->
            let symbolicLinkDirectory = Path.GetDirectoryName(symbolicLink)

            if not (Directory.Exists(symbolicLinkDirectory)) then
                Directory.CreateDirectory(symbolicLinkDirectory)
                |> ignore

            let result =
                Cli.Wrap("ln").WithArguments(
                    $"-s {actualPath} {symbolicLink}"
                )
                    .WithValidation(
                    CommandResultValidation.None
                )
                    .ExecuteBufferedAsync()
                    .Task
                |> Async.AwaitTask
                |> Async.RunSynchronously

            if result.ExitCode <> 0 then
                Result.Error $"[#d78700]Error while creating symlinks[/]:\n[#ff8700]{result.StandardError}[/]"
            else
                Ok()
        | Common.IsWindows ->
            let result =
                Cli.Wrap("cmd.exe").WithArguments(
                    $"""/C "mklink /j {symbolicLink} {actualPath}" """
                )
                    .WithValidation(
                    CommandResultValidation.None
                )
                    .ExecuteBufferedAsync()
                    .Task
                |> Async.AwaitTask
                |> Async.RunSynchronously

            if result.ExitCode <> 0 then
                Result.Error $"[#d78700]Error while creating symlinks[/]:\n[#ff8700]{result.StandardError}[/]"
            else
                Ok()
        | Common.Unsupported value ->
            Result.Error
                $"Could not write symlink {symbolicLink} -> {actualPath}, for more information please see https://github.com/dotnet/runtime/issues/24271"

    let trySetPermissionsUnix (dir: string) =
        task {
            let! dirPerm =
                Cli.Wrap("chmod").WithArguments(
                    $"-R +x {dir}"
                )
                    .WithValidation(
                    CommandResultValidation.None
                )
                    .ExecuteBufferedAsync()
                    .Task

            let childPerm =
                let dirs = Directory.EnumerateFiles(dir)

                dirs
                |> Seq.map (fun file ->
                    Cli.Wrap("chmod").WithArguments(
                        $"+x {file}"
                    )
                        .ExecuteBufferedAsync()
                        .Task)

            let! children = Task.WhenAll(childPerm)

            let exitCodes =
                children
                |> Seq.fold (fun value result -> result.ExitCode + value) 0

            let errors =
                [ dirPerm.StandardError
                  yield! children |> Seq.map (fun res -> res.StandardError) ]

            if (dirPerm.ExitCode + exitCodes) <> 0 then
                return Error errors
            else
                return Ok()
        }

    let private symLinkDelegate (symbolicLink: string) (actualPath: string) =
        match createSymlink actualPath symbolicLink with
        | Ok () -> ()
        | Error err -> AnsiConsole.MarkupLine err

    let extractContents (os: CurrentOS) (source: string) (output: string) =
        match os with
        | Windows ->
            try
                ZipFile.ExtractToDirectory(source, output)
            with
            | :? IOException as ex ->
                if
                    ex.Message.Contains("already exists.")
                    || ex.InnerException.Message.Contains("already exists")
                then
                    ()
                else
                    reraise ()
        | _ ->
            let sourceFile = FileInfo(source)
            use sourceStr = sourceFile.OpenRead()
            use reader = ReaderFactory.Open(sourceStr)
            let opts = ExtractionOptions()
            opts.ExtractFullPath <- true
            opts.Overwrite <- true
            opts.WriteSymbolicLink <- new ExtractionOptions.SymbolicLinkWriterDelegate(symLinkDelegate)
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
        let dir = Common.getHome ()

        Directory.CreateDirectory(dir)

    let deleteFile (path: string) = File.Delete(path)

    let getParentDir (file: string) =
        let file = FileInfo(file)
        file.DirectoryName

    let deleteSymlink (path: string) (os: CurrentOS) =
        let cmd =
            match os with
            | Windows ->
                Cli
                    .Wrap("cmd.exe")
                    .WithArguments($"""/C rmdir {path}""")
            | _ ->
                Cli
                    .Wrap("unlink")
                    .WithArguments(
                        $"""{if path.EndsWith("/") then
                                 path.Substring(0, path.Length - 1)
                             else
                                 path}"""
                    )

        cmd
            .WithValidation(
                CommandResultValidation.None
            )
            .ExecuteBufferedAsync()
            .Task
        |> Async.AwaitTask
        |> Async.RunSynchronously

    let getCurrentNodeVersion (os: CurrentOS) =
        let cmd =
            match os with
            | Windows -> Cli.Wrap("node.exe")
            | _ -> Cli.Wrap("node")

        cmd.WithArguments("--version").WithValidation(
            CommandResultValidation.None
        )
            .ExecuteBufferedAsync()
            .Task

    let deleteDir (path: string) =
        let dir = DirectoryInfo(path)
        dir.Delete(true)

    let removeHomeDir () =
        let dir = NvmFs.Common.getHome ()
        deleteDir dir

    let fullPath (path: string, paths: string list) =
        let paths = path :: paths
        Path.GetFullPath(Path.Combine(paths |> Array.ofList))

    let getSymlinkPath (codename: string) (directory: string) (os: CurrentOS) =
        fullPath (
            Common.getHome (),
            [ $"latest-{codename}"
              $"{directory}"
              if os = Windows then "" else "bin" ]
        )


    let getIndex () =
        task {
            let path = Common.getHome ()

            use file = File.OpenText(fullPath (path, [ "index.json" ]))

            let! content = file.ReadToEndAsync()

            return
                match Decode.fromString (Decode.array NodeVerItem.Decoder) content with
                | Ok res -> res
                | Error err -> [||]
        }

    let removeExtension (file: string) =
        let file = FileInfo(file)
        file.FullName.Substring(0, file.FullName.Length - file.Extension.Length)

    let getChecksumForVersion (checksumfile: string) (version: string) (os: CurrentOS) (arch: string) =
        let line =
            let ext =
                match os with
                | Windows -> "zip"
                | _ -> "tar.gz"

            $"{Common.getVersionDirName version os arch}.{ext}"

        let file = FileInfo(checksumfile)
        use file = file.OpenText()
        let content = file.ReadToEnd()
        let lines = content.Split('\n')

        lines
        |> Array.tryFind (fun l -> l.Contains(line))
        // if we find the line get the checksum which should be the first item
        |> Option.map (fun l -> l.Split(' ') |> Array.tryHead)
        |> Option.flatten

    let appendToBashRc (lines: string list) =
        let path =
            fullPath (Environment.GetFolderPath(Environment.SpecialFolder.Personal), [ ".bashrc" ])

        use file = File.AppendText(path)

        for line in lines do
            file.WriteLine(line)

    let codenameExistsInDisk (codename: string) =
        let home = DirectoryInfo(Common.getHome ())

        home.GetDirectories()
        |> Array.exists (fun dir -> dir.Name.Contains(codename))

    let versionExistsInDisk (codename: string) (version: string) =
        let home = DirectoryInfo(Common.getHome ())
        let directories = home.GetDirectories()

        let exists =
            directories
            |> Array.exists (fun dir -> dir.Name.Contains(codename))

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
        |> Array.map (fun dir -> dir.GetDirectories())
        |> Array.reduce Array.append
        |> Array.filter (fun dir -> not (dir.FullName.Contains("bin")))
        |> Array.map (fun dir -> dir.Name.Split('-').[1])
        |> Array.sortDescending
