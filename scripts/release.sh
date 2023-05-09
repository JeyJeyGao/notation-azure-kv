#!/bin/bash

set -e

# Check if the tag name is provided
if [ -z "$1" ]; then
    echo "Usage: $0 <tag_name>"
    exit 1
fi

tag_name="$1"
version=${tag_name#v}
project_name="notation-azure-kv"
mkdir -p ./bin/publish && output_dir=$(realpath ./bin/publish)
mkdir -p ./bin/artifacts && artifacts_dir=$(realpath ./bin/artifacts)
checksum_name=$artifacts_dir/${project_name}_${version}_checksums.txt

declare -a runtimes=("linux-x64" "linux-arm64" "osx-x64" "osx-arm64" "win-x64")

# Publish for each runtime
commitHash="$(git log --pretty=format:'%h' -n 1)"
for runtime in "${runtimes[@]}"; do
    dotnet publish ./Notation.Plugin.AzureKeyVault \
        --configuration Release \
        --self-contained true \
        -p:PublishSingleFile=true \
        -p:CommitHash="$commitHash" \
        -p:Version="$version" \
        -r "$runtime" \
        -o "$output_dir/$runtime"
done

# Package the artifacts and create checksums
declare -a artifacts=()
mkdir -p "${artifacts_dir}"
for runtime in "${runtimes[@]}"; do
    if [[ $runtime == *"win"* ]]; then
        ext="zip"
    else
        ext="tar.gz"
    fi

    # Apply the runtime name mapping
    mapped_runtime="${runtime/x64/amd64}"
    mapped_runtime="${mapped_runtime/win/windows}"
    mapped_runtime="${mapped_runtime/osx/darwin}"
    mapped_runtime="${mapped_runtime/-/_}"

    artifact_name="${artifacts_dir}/${project_name}_${version}_${mapped_runtime}.${ext}"
    binary_dir="$output_dir/$runtime"

    if [[ $ext == "zip" ]]; then
        # To have flat structured zip file, zip the binary and then update zip 
        # to include the LICENSE file
        (cd "${binary_dir}" && zip -x '*.pdb' -r "${artifact_name}" .) && zip -ur "${artifact_name}" LICENSE
    else
        tar --no-xattrs -czvf "${artifact_name}" --exclude='*.pdb' -C "${binary_dir}" . -C ../../.. LICENSE
    fi

    (cd "${artifacts_dir}" && shasum -a 256 "$(basename "${artifact_name}")" >>"${checksum_name}")

    # Add the artifact to the list
    artifacts+=("${artifact_name}")
done

# Create a release using GitHub CLI
if [[ "$tag_name" == *"-"* ]]; then
    # v1.0.0-rc.1 is a pre-release
    gh release create --title "${tag_name}" --prerelease --draft "${tag_name}" "${artifacts[@]}" "${checksum_name}"
else
    # v1.0.0 is a release
    gh release create --title "${tag_name}" --draft "${tag_name}" "${artifacts[@]}" "${checksum_name}"
fi
