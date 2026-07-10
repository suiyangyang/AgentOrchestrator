# Edit File Skill

本地使用一个可自动发现的 Skill 作为默认文件编辑能力：

- 路径：`C:\Users\80714\.codex\skills\edit-file`
- 名称：`edit-file`

## 目的

当任务需要改写中文或混合编码文本文件，且普通写文件流程容易把 BOM、编码或行尾写坏时，优先使用这个 Skill。

它参考了 `anomalyco/opencode` 的 `write` / `edit` / `apply_patch` / `bom` 实现，保留了几个最关键的行为：

- 先拆 BOM，再处理正文
- 已有文件优先保留 BOM
- 编辑前统一行尾，写回时恢复原文件行尾
- 局部替换使用分层匹配，而不是只做单一精确匹配

## 组成

- `scripts/inspect_text_file.py`：检查编码、BOM、行尾
- `scripts/write_text_file.py`：整文件写入
- `scripts/edit_text_file.py`：局部替换
- `scripts/edit_lines.py`：按连续行范围替换
- `references/opencode-investigation.md`：调查结论

## 使用建议

- 不确定文件现状时，先 `inspect`
- 全量改写时，用 `write`
- 文本块替换时，用 `edit`
- 明确给了行号范围时，用 `edit_lines`
- 普通文本写入、整文件改写、按行替换，默认优先这个 Skill
- 结构化的小范围代码改动，优先 `apply_patch`
- 遇到中文仓库时，不要默认所有文件都是 UTF-8
