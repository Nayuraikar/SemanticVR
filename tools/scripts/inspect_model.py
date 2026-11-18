import onnxruntime as ort, collections
s=ort.InferenceSession("model/model.onnx",providers=["CPUExecutionProvider"])
for i in s.get_inputs(): print("IN",i.name,i.shape,i.type)
for o in s.get_outputs(): print("OUT",o.name,o.shape,o.type)
