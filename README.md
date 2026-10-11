# IslandAgent

ClassIsland 的 AI 插件。接入任意 OpenAI 兼容接口，用对话的方式改课表、调设置。

同时支持 ClassIsland 1.x（WPF）和 2.x（Avalonia）。

## 能做什么

**改课表**：问它「今天第 3 节是什么」，或者说「明天第 2 节换化学」「周三下午全调成自习」，它会直接写进当前档案。

**改设置**：主题、透明度、缩放、通知开关、上课隐藏、时间偏移，都能用一句话改掉，改完立即生效。

**发图片**：把课表照片丢进对话框，让它读出科目和时间段再排课。图片以 base64 随消息一起发出，所以**需要选一个支持视觉的模型**，纯文本模型会读不出来。

**记东西**：跟它说「记住化学老师是王老师」，以后会一直记得。记忆内容在设置页可以直接编辑。

## 安装

在 [Releases](https://github.com/MEMZ-Chrome/ClassIsland-AIAuto/releases) 下载对应的包：

- ClassIsland 2.x → `ClassIsland.Automation.V2.cipx`
- ClassIsland 1.x → `ClassIsland.Automation.V1.cipx`

`.cipx` 可以直接在 ClassIsland 里安装，或者解压到 `Plugins/` 目录。

## 配置

装好后打开「应用设置 → IslandAgent设置」。

**第一步**，在「模型服务」里点一个预置模板，或者自己填：

| 预置 | 地址 |
| --- | --- |
| DeepSeek | `https://api.deepseek.com` |
| OpenRouter | `https://openrouter.ai/api/v1` |
| 小米 MiMo | `https://api.xiaomimimo.com/v1` |
| OpenAI | `https://api.openai.com/v1` |
| 自建 Ollama | `http://localhost:11434/v1` |

点预置只会填别名和地址，**密钥要自己填**。任何 OpenAI 兼容接口都能用，不限于上面这些。

**第二步**，填 API Key，点「自动获取」把模型列表拉下来，勾选要用的。

插件内置的模型列表是空的，没有硬编码任何模型名——模型会过时，所以一律从接口实时拉取，也可以手动输入。

**第三步**，托盘图标右键 → 「AI助手」，开始对话。

## 对话里能用的工具

模型通过下面这些工具操作 ClassIsland，一次对话可以连续调用多个：

| 工具 | 作用 |
| --- | --- |
| `get_current_time` | 取系统时间、日期、星期，避免模型靠猜 |
| `get_profile_summary` | 档案概览：科目、时间表、课表列表 |
| `get_schedule_details` | 具体课表内容，含节次、科目、老师 |
| `upsert_subjects` | 增改科目，含简称、任课老师、是否室外课 |
| `create_time_layout` | 建或替换作息时间表 |
| `set_class_plan` | 配某天的固定课表 |
| `setup_temp_class_plan` | 临时调课，支持单节替换和整天替换 |
| `get_app_settings` / `update_app_settings` | 读写 ClassIsland 应用设置 |
| `get_plugin_settings` / `update_plugin_settings` | 读写插件自身设置（供应商、模型、记忆、提示词） |
| `navigate_app_page` | 跳转到 ClassIsland 的某个页面 |
| `update_memory` | 写入长期记忆 |
| `execute_command` | 执行命令行，**默认关闭** |

`execute_command` 需要手动开启（高级偏好 → 允许 AI 执行命令行命令），关着的时候这个工具根本不会暴露给模型。

## 安全

设置页可以设固定密码或 TOTP 动态口令，防止别人在大屏上翻你的密钥。

关于 TOTP 有个设计上的取舍：**没完成首次验证前，页面不会被锁定**。因为在学校可能一时连不上手机同步验证器，如果生成密钥就先锁上，人就彻底进不去了。所以只有验证码验证通过、TOTP 正式激活之后，保护才生效。

API 密钥和 TOTP 密钥在磁盘上是加密存的（`enc:v1:` 前缀）。

## 构建

没有本地构建的流程，改完推 `main`，GitHub Actions 会编译并产出安装包。Actions 里下 `ClassIsland-Automation-Plugins` 这个 artifact。

打 tag（`1.0.0.0` 或 `v*`）会额外发一个 Release。

## 项目结构

```
ClassIsland.Automation.Core   共享逻辑：AI 客户端、工具执行器、加密、TOTP
ClassIsland.Automation.V1     1.x 外壳（WPF + MaterialDesign）
ClassIsland.Automation.V2     2.x 外壳（Avalonia + FluentAvalonia）
```

业务逻辑都在 Core，V1/V2 只负责界面和宿主适配。两个外壳的元素命名是一套，改动时注意别改 `Name`，代码里按名字取控件。

## 作者

MEMZ-Chrome · [Bilibili](https://space.bilibili.com/1353783215)

MIT License
