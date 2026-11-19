#!/bin/zsh
# usage: try_version.sh <package-version>
cd ~/Projects/SemanticVR/tools
V=$1
sed -i '' -E "s|\"com.unity.ai.inference\": \"[^\"]+\"|\"com.unity.ai.inference\": \"$V\"|" unity_infer_test/Packages/manifest.json
export REF_DIR=$PWD/ref
rm -f ref/unity_*
"/Applications/Unity/Hub/Editor/6000.3.28f1/Unity.app/Contents/MacOS/Unity" -batchmode -quit -projectPath "$PWD/unity_infer_test" -executeMethod InferenceCompatTest.Run -logFile "$PWD/compat_$V.log"
echo "== version $V (exit $?)"
grep -a -A2 "\"com.unity.ai.inference\": {" unity_infer_test/Packages/packages-lock.json | grep version
grep -a -E "^\[COMPAT\] (RESULT|.*first run|.*steady)|error CS" compat_$V.log | cut -c1-160
#OLD: grep -a -E "COMPAT\] (RESULT|.*first run|.*steady|.*(dets|labels) shape)|error CS|Unsupported|not supported" compat_$V.log | grep -a -v "LayerNormalization" | cut -c1-200
for b in CPU GPUCompute; do [ -f ref/unity_${b}_masks.bin ] && ./venv/bin/python -I scripts/cmp_masks.py $b; done
