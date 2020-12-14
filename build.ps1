Param([switch]$pack, [string] $rid = 'win10-x64')

if ($pack) {
    dotnet pack -c Release -o dist
} else {
    dotnet publish -c Release -r $rid --self-contained true -p:PublishSingleFile=true -o "dist/$rid"
}

