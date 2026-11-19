#!/bin/zsh
# usage: dev_batch.sh <logname> <executeMethod> [extra unity args...]  (runs in tools/unity_dev, never the main project)
cd ~/Projects/SemanticVR/tools
L=$1; M=$2; shift 2
"/Applications/Unity/Hub/Editor/6000.3.28f1/Unity.app/Contents/MacOS/Unity" -batchmode ${NOQUIT:+} $([ -z "$NOQUIT" ] && echo -quit) -projectPath "$PWD/unity_dev" -executeMethod $M -logFile "$PWD/$L.log" "$@"
echo "exit $?"
