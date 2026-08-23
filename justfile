# justfile
default: all

all: clean build

[windows]
set windows-shell := ["nu.exe", "-c"]

[windows]
clean:
  rm --force --recursive ./sjtf.cli/bin/Release/net10.0/win-x64/publish

build:
  dotnet publish -r win-x64 -c Release
