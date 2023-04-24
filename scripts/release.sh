#!/bin/bash

set -ex

# Check if the tag name is provided
if [ -z "$1" ]; then
    echo "Usage: $0 <tag_name>"
    exit 1
fi

tag_name="$1"
version=${tag_name#v}
project_name="notation-azure-kv"
output_dir=$(realpath ./publish)
artifacts_dir=$(realpath ./artifacts)
checksum_name=$artifacts_dir/${project_name}_${version}_checksums.txt

declare -a runtimes=("linux-x64" "linux-arm64" "osx-x64" "osx-arm64" "win-x64")

cd ./Notation.Plugin.AzureKeyVault
# Publish for each runtime
for runtime in "${runtimes[@]}"; do
    dotnet publish --configuration Release --self-contained true -r $runtime -o $output_dir/$runtime
done
cd -

# Package the artifacts and create checksums
declare -a artifacts=()
mkdir -p $artifacts_dir
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

    artifact_name="${artifacts_dir}/${project_name}_${version}_${mapped_runtime}.${ext}"
    binary_dir="$output_dir/$runtime"

    if [[ $ext == "zip" ]]; then
        # to have flat structured zip file, zip the binary and then update zip to
        # include the LICENSE file
        (cd $binary_dir && zip -x '*.pdb' -r $artifact_name .) && zip -ur $artifact_name LICENSE
    else
        tar czvf $artifact_name --exclude='*.pdb' -C ${binary_dir} . -C ../.. LICENSE
    fi

    (cd $artifacts_dir && sha256sum $(basename $artifact_name) >>${checksum_name})

    # add the artifact to the list
    artifacts+=($artifact_name)
done
echo ${artifacts[@]}

# Authenticate and create a pre-release using GitHub CLI
gh auth login --with-token <(echo $GITHUB_TOKEN)
gh release create --title "$tag_name" --prerelease $tag_name ${artifacts[@]} ${checksum_name}