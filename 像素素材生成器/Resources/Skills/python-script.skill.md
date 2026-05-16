---
name: python-script
displayName:
  zh: Python 脚本处理
  en: Python Script Processing
description:
  zh: 使用 Python 脚本批量生成或修改 .node.json 文件、处理参数配置等
  en: Use Python scripts to batch generate/modify .node.json files and process configurations.
category: BuiltIn
tags: [python, script, batch, automation]
kind: instructions
---

# Python 脚本处理

## 步骤 1: 批量生成节点文件

使用 Python 脚本批量生成多个 `.node.json` 文件。脚本需处理 JSON 序列化并写入正确的目录。

```python
import json, os

nodes_dir = './Nodes/Custom'
os.makedirs(nodes_dir, exist_ok=True)

node = {
    "formatVersion": 2,
    "identity": {
        "typeName": "BatchNode",
        "displayName": { "zh-Hans": "批量节点", "en": "Batch Node" },
        "category": "Custom"
    },
    "ports": {
        "inputs": [],
        "outputs": [{ "name": { "zh-Hans": "输出", "en": "Output" }, "type": "Image" }]
    },
    "script": { "language": "csharp", "code": "return PixelBuffer.CreateSolid(32,32,255,128,64);" }
}

with open(os.path.join(nodes_dir, "BatchNode.node.json"), 'w', encoding='utf-8') as f:
    json.dump(node, f, ensure_ascii=False, indent=2)
print('Node file created')
```

**预期结果**: 节点文件生成在 Custom/ 目录中

## 步骤 2: 批量更新参数

读取现有 `.node.json` 文件，批量修改参数后写回。

```python
import json, os, glob

nodes_dir = './Nodes/Custom'
for f in glob.glob(os.path.join(nodes_dir, '*.node.json')):
    with open(f, 'r', encoding='utf-8') as fh:
        data = json.load(fh)
    for param in data.get('parameters', []):
        if 'strength' in param.get('name', {}).get('en', '').lower():
            param['default'] = 0.8
    with open(f, 'w', encoding='utf-8') as fh:
        json.dump(data, fh, ensure_ascii=False, indent=2)
print(f'Updated {len(glob.glob(os.path.join(nodes_dir, "*.node.json")))} files')
```

**预期结果**: 所有节点的强度参数默认值更新为 0.8
