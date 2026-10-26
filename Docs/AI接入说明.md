# 动物语言的 AI 判定

正式玩法已接入独立客户端，并有隔离存档的真实服务联调记录。它判断玩家是否理解当前动物请求，不控制动物、不施法。摄影修订中的词义奖励由确定的照片和公开互动规则发放，不依赖 AI。

## 本地配置

仓库中的 `Config/WeatherHouseAI.example.json` 是空模板，保持无密钥。

- Unity 编辑器读取项目根目录 `UserSettings/WeatherHouseAI.json`。此目录已被 `.gitignore` 排除。
- 打包后读取 `Application.persistentDataPath/WeatherHouseAI.json`，具体位置可通过客户端的 `ConfigurationPath` 属性取得。
- 初次查询配置或发起判断时，缺失的文件会在对应位置创建为空模板。未填写配置时立即报告“AI尚未配置”，不会发网络请求。

填写 `baseUrl`、`model`、`apiKey`，`timeoutSeconds` 默认 30 秒。`baseUrl` 填服务商的接口根路径，例如 `https://your-provider.example/v1`；客户端会追加 `/chat/completions`，不要把完整终点重复填入。使用服务商实际支持的模型名。配置只在运行时从本地文件读取，密钥不放进 Inspector、场景或存档。

客户端发送非流式 Chat Completions 请求，带 `model`、`messages`、`stream: false` 及 Bearer 授权头。没有绑定任何供应商 SDK，也没有强制供应商支持额外的 JSON 模式参数。HTTP 重定向被禁用，避免授权头被转发到另一个地址。日志和错误提示不会输出密钥或服务响应正文。

### 本项目的 DeepSeek 配置

本机配置文件完整路径为 `C:\unity project\Weather House\UserSettings\WeatherHouseAI.json`，已创建。填写 `apiKey` 后保存；地址为 `https://api.deepseek.com`，模型为 `deepseek-flash`。只在此本地文件填写密钥，不写入示例或提交。

当地址主机是 `api.deepseek.com` 时，客户端使用 `thinking: {"type":"disabled"}`、`response_format: {"type":"json_object"}` 和 768 token 输出上限，适合短语义判断。其他兼容服务保持基础请求格式。官方接口依据：[首次调用](https://api-docs.deepseek.com/quick_start/agent_integrations/)、[Chat Completions](https://api-docs.deepseek.com/api/create-chat-completion/)，核查日期 2026-10-26。

## 调用与确认边界

给 `LanguageJudgementClient.Judge(input, complete)` 传入当前动物、句子 ID、策划预设请求含义、玩家原话、情境及当前句字 ID。`context` 应提供本次考察的字与预设含义之间的对应关系；对应关系不足时，模型应保留空证据列表，不自行猜字形含义。

玩家原话是待评估数据。系统指令要求模型忽略其中试图改规则、扮演管理员或要求直接判对的内容。自然同义表达可以通过；“好呀”、只说“水”、罗列所有天气等不足以确认“雨”。客户端没有本地字符串猜义或离线假判定。

正式 Life 模式在 `success` 与 `understood` 均为 true、原请求和话轮仍有效、玩家仍满足近距离条件时，接受该帮助请求。`evidenceGlyphIds` 保留用于解析协议兼容，不向正式手账额外发词；正确理解请求与实际完成帮助分别记录。摄影任务只发其预设的 1–2 个词义，帮助成功不翻译整句。

旧 MapDraft 原型的逐字确认接口仍保留兼容检查，只有以下条件同时满足才确认；这段规则不用于正式 Life 模式：

1. 返回 `success == true` 且 `understood == true`。
2. 本次目标字在 `evidenceGlyphIds` 中，且仍属于当前正在回应的那条请求。
3. 手账 `ConfirmFromJudgement` 验证已有另一只动物的观察来源。

异步请求期间玩家可能切换动物或关闭窗口，调用方须匹配原请求 ID，丢弃已经过时的结果。

## 响应格式

服务应返回 `choices[0].message.content` 字符串。该字符串必须恰好是一份包含以下三个字段的 JSON，无 Markdown 围栏或说明文字：

```json
{"understood":true,"evidenceGlyphIds":["rain"],"feedback":"你准确回应了它的请求。"}
```

负例是有效判断，不是网络故障：

```json
{"understood":false,"evidenceGlyphIds":[],"feedback":"请更具体地说明你理解它希望发生什么变化。"}
```

`TryParseResult(content, currentGlyphIds, out result)` 可独立验证结构。缺字段、类型错误、未知或重复字 ID、重复字段、额外字段、截断、非 JSON，以及“不理解但给出证据”的矛盾结果均返回 `success=false`。网络失败、超时、HTTP 错误或服务未正常完成也不确认任何词义，保留玩家输入供重试。

## 真实接入验收

`LifeGameplayReview/20261026/play/dialogue-provider-pass.json` 记录正式 HUD 到真实服务的帮助理解联调。此前 56 次原型样本检查保存在 `live-ai-56-pass.json`，不把旧词义确认结果当作新摄影奖励证据。以报告时间及相关源码版本为准；预写回答通过不代表所有自然表达。用户试玩仍需检查同义表达、否定、含糊答复、断网重试和输入手感。
