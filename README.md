# Arena：面向智能体比赛的 C# 游戏引擎原型

这是一个可运行的可行性 demo：**固定帧模拟 + 3D 观战 + 纯图像智能体观察 + gRPC 控制**。第一款示例游戏是 3D 躲猫猫。引擎核心、渲染器和协议独立于躲猫猫规则，后续比赛可以替换游戏模块。

技术栈：**.NET 10 / C#、Stride 4.4、Stride.BepuPhysics、ASP.NET Core gRPC、Protocol Buffers、xUnit**。Stride 负责场景、材质、GPU 相机和观战窗口，Bepu 负责角色物理。场景通过代码创建，无需安装 Game Studio 或下载模型资源；首次构建会下载 NuGet 包，并编译 Stride 的内置着色器和字体。

## 快速运行

要求：.NET 10 SDK、可用的图形设备及驱动。目前验证环境为 **Windows x64 + Direct3D 11 + NVIDIA RTX 5070 Ti**。`--headless` 使用 Stride 的 `GameContextHeadless`，不创建系统窗口，但仍初始化图形设备、渲染并回读图像。CPU-only 和 Linux/Vulkan 部署尚未验证。

在项目根目录运行：

```powershell
dotnet build Arena.slnx --configuration Release
dotnet run --no-build --configuration Release --project src/Arena.Demo
```

窗口中红色是寻找者，绿色是躲藏者，均使用 Bepu 胶囊角色。默认双方由纯像素基线策略控制。决策默认 **20 Hz**，每个决策帧执行 **3 个约 1/60 秒的物理步**，窗口刷新与模拟推进独立。等待动作或只读取图像时，手动模式的世界保持静止。首次启动会预热着色器，终端出现 `Arena ready` 后即可连接。

| 操作 | 效果 |
| --- | --- |
| 鼠标右键拖动 / 滚轮 | 观战模式下环绕相机 / 缩放 |
| Tab | 切换完整观战视角、智能体 1 / 2 实际收到的第一人称图像 |
| Space | 暂停 / 继续实时模拟 |
| N | 手动模式推进一帧（启动时加 `--manual`） |
| R | 使用同一 seed 重置，episode 自增 |
| Esc / 关闭窗口 | 退出，同时停止 gRPC 服务 |

UI 使用 Stride 内置调试字体显示英文；项目说明使用中文。

本机观战截图：

![3D 躲猫猫观战窗口](docs/demo.png)

## 查看智能体实际看到的图像

直接打开躲藏者的第一人称图像：

```powershell
dotnet run --configuration Release --project src/Arena.Demo -- --view 2
```

`--view 0` 是完整观战，`--view 1` 是寻找者，`--view 2` 是躲藏者。窗口内按 `Tab` 循环切换。寻找者前 3 秒是全黑画面，这是躲藏期规则。可以用 `--manual --view 2` 启动后按 `N` 逐帧观察内置智能体。

![智能体第一人称图像预览](docs/agent-view.png)

画面矩形内直接显示 gRPC 使用的 RGB 字节，采用等比例最近邻放大。窗口标题、帧号与操作提示在图像矩形外，不属于智能体输入。这个模式没有角色标签、俯视地图或观战信息叠加；相机随角色的 `turn` 动作旋转。

### 纯图像输入约定

`AgentGateway.Observe` 只返回 `ImageObservation`：

| 字段 | 含义 |
| --- | --- |
| `rgb24` | 320 × 180 的 RGB 图像，紧密排列的原始字节，无压缩 |
| `width` / `height` | 图像尺寸 |
| `episode` / `frame` | 用于提交动作的对局与帧标识 |
| `finished` | 对局生命周期结束标志 |

**游戏状态的唯一观察来源是图像。** 接口不返回地图实体、坐标、朝向、角色 ID、可见对象列表、深度、分割掩码、阶段、倒计时、胜者、事件或 seed。上述编号、尺寸、结束标志只用于传输、调度和停止客户端。角色分配、动作含义等是赛前配置，示例 bot 用 `--role seeker|hider` 指定策略角色。

图像按从上到下、从左到右排列，每个像素依次为 R、G、B 三个 8 位通道，无行填充。长度为 `width * height * 3`，默认 172800 字节；例如 Python / NumPy 可用 `np.frombuffer(reply.rgb24, dtype=np.uint8).reshape(reply.height, reply.width, 3)` 读取。图像不含 EXIF 或其他状态元数据。

相机位于角色眼睛位置，垂直视场 60°（16:9 时水平约 91.5°），水平朝向可转动，最远 40 单位。所有场景几何参与逐像素遮挡。身后的物体和被墙完全遮住的物体不可见；部分露出则只产生露出部分的像素。当前没有俯仰控制。每个角色、每个逻辑帧的图像缓存供窗口、gRPC 与内置策略共同使用。

协议包已从 `arena.v1` 升级为 **`arena.v2`**，这是不兼容修改。旧版客户端需要根据新的 `.proto` 重新生成并改为读取 `ImageObservation`；旧观察 RPC 不再注册。

## 运行一个外部智能体

启动服务器后，终端会打印各自独立的 `Seeker token`、`Hider token` 和 `Referee token`。默认监听 `http://127.0.0.1:50051`，使用本机 HTTP/2。

打开第二个终端，用服务器打印的寻找者 token 运行：

```powershell
dotnet run --project samples/Arena.Bot -- --token "替换为 Seeker token"
```

智能体每次读取自己的 RGB 图像，再给下一帧提交移动和转向动作。接受的外部动作会覆盖该帧的内置策略。实时模式存在图像计算与网络延迟，示例客户端会处理帧已过期的情况。控制躲藏者时使用它的 token 并加 `--role hider`。

如需完全由外部智能体控制，服务器启动时加 `--no-bots`；该模式下没有收到动作的角色本帧移动意图为零。动作不会自动持续，但重力、惯性和碰撞仍按物理规则处理。

## 用比赛调度方式跑一整局

手动模式中，时间只在裁判调用 `Step` 时推进。双方可以基于同一帧思考，避免机器性能和网络延迟直接改变模拟进度。

第一个终端：

```powershell
dotnet run --configuration Release --project src/Arena.Demo -- --manual --no-bots
```

第二个终端：

```powershell
dotnet run --project samples/Arena.Bot -- --match --token "Seeker token" --hider-token "Hider token" --referee-token "Referee token" --delay-ms 50
```

`--match` 是演示裁判进程，顺序为：重置 → 双方图像观察 → 双方提交动作 → 推进 1 帧 → 重复直到胜负确定。两种策略分别只读取自己的图像与调度信息。`--delay-ms 50` 便于看清动画；删掉它可尽快完成模拟。裁判拥有完整状态，参赛策略不会接收到该状态；控制台带 `[referee]` 的结果来自裁判接口。

可用这一条 PowerShell 命令自动构建、启动无窗口服务、通过独立进程完成 15 秒的模拟对局，并清理服务进程：

```powershell
powershell -ExecutionPolicy Bypass -File scripts/demo-match.ps1
```

该脚本使用 Release 构建，临时生成并传递 token，完成后恢复环境变量。通过 `-Port 50052 -Seed 123 -RoundSeconds 60` 可以更改对局设置。构建和服务器日志保存在 `artifacts/`，脚本会显示完整对局吞吐与 GPU 图像耗时。到达终局后服务自动退出。

无窗口服务器也可以单独运行：

```powershell
dotnet run --configuration Release --project src/Arena.Demo -- --headless --manual --no-bots
```

## 分层结构

```text
src/
  Arena.Engine/            固定帧、动作队列、引擎线程调度、图像与角色物理契约
  Arena.Game.HideAndSeek/  躲猫猫地图、角色控制意图、相机、胜负和像素基线策略
  Arena.Protocol/         与游戏无关的 .proto；生成 C# 服务端与客户端契约
  Arena.Rendering/        Stride 主循环、Bepu 角色世界、GPU 图像回读和观战
  Arena.Demo/             组合游戏、Kestrel、gRPC 与引擎生命周期
samples/
  Arena.Bot/              独立 gRPC 智能体、演示裁判
tests/
  Arena.Tests/            帧规则、真实 Bepu/GPU 与 HTTP/2 集成测试
```

```mermaid
flowchart LR
    Agent[外部智能体] -->|图像观察 / 指定帧动作| RPC[gRPC 接口]
    Referee[裁判进程] -->|Step / Reset| RPC
    RPC --> Queue[有界命令队列]
    Queue --> Engine[Stride 线程上的固定决策帧]
    Engine --> Game[IGameModule 游戏规则]
    Game -->|角色移动意图| Physics[Bepu CharacterComponent]
    Physics -->|子步后的位置| Game
    Game -->|仅服务器可用的场景与相机| Camera[Stride GPU 渲染到纹理]
    Camera -->|RGB 图像| Engine
    Engine --> Renderer[通用 3D 观战窗口]
```

换下一届游戏时，实现 `IGameModule`：定义角色、初始化、每帧规则、裁判快照 `Capture()` 和服务器内部使用的角色相机场景 `CaptureCamera(agentId)`，然后在 `DemoHost` 中注册。后者提供眼睛位置、朝向和可渲染几何，只有 `IObservationRenderer` 接收它；智能体控制器的 `Decide` 参数类型始终是 `ImageObservation`。

`SceneEntity` 支持三维位置、尺寸、颜色和盒体 / 球体 / 胶囊原语；通用操作是移动向量、`turn` 和游戏自定义 `ability` 字符串。`ICharacterWorld` 提供角色移动、物理推进、落地和遮挡查询，`IObservationRenderer` 提供 GPU 图像。

`Arena.Engine` 使用 `System.Numerics`，不引用 Stride、ASP.NET Core 或躲猫猫项目。`Arena.Rendering` 引用 Stride 与引擎契约，不引用具体游戏。示例 bot 为复用基线策略引用了游戏项目，但不加载 Stride；参赛者可以改写 `BaselineAgent.Decide`，或者仅使用 protobuf 生成其他语言的客户端。

## Stride + Bepu 后端实现

- **场景与材质**：使用 Stride Entity、ModelComponent、程序化模型、材质和灯光。当前是 code-only 工程，可直接 `dotnet build`；没有额外依赖 Community Toolkit 的旧版包。
- **物理**：使用 `Stride.BepuPhysics` 的 `CharacterComponent`、`StaticComponent`、胶囊/盒体碰撞和射线查询。角色具有重力、落地约束、墙体阻挡、跳跃及相互碰撞。重置时重建物理世界，清除旧接触约束。
- **决策与物理步**：子步数为 `ceil(60 / tickRate)`，每个子步为 `1 / (tickRate * 子步数)` 秒；默认 20 Hz 对应 3 个约 1/60 秒子步。底层 TimeSpan 有亚微秒级取整。
- **线程**：`SimulationDispatcher` 用容量 256 的命令队列将 RPC 请求交给 Stride 线程。每批最多处理 32 个命令，取消且尚未开始的命令不会执行。已经开始执行的动作/步进可能在 RPC 超时后完成，应通过帧号重读确认。
- **显式步进**：`StrideArenaGame` 驱动 `RawTick`，只有物理推进传入非零模拟时间。读取观察、窗口刷新与等待客户端都不推进物理；角色渲染插值关闭，图像对应当前完成帧。
- **图像**：320×180 RGBA sRGB 渲染目标 + 深度缓冲，复用 staging 纹理进行同步回读，去掉 Alpha 后发送 RGB24。同步回读是当前原型的帧完成屏障，避免返回上一帧的图片。图像按角色和决策帧缓存。
- **观战**：同一个 Stride 后端绘制俯视场景和智能体图像预览。HUD 仅画在窗口目标上，观察纹理不包含 HUD。

### 性能记录

本机 Windows / RTX 5070 Ti / Release，运行 `scripts/demo-match.ps1`，默认地图、2 个外部像素策略、320×180 RGB、15 秒模拟时间：

| 指标 | 一次实测 |
| --- | --- |
| 决策帧 / 图像 / 物理子步 | 300 / 600 / 900 |
| 客户端对局墙钟耗时 | 约 1.37 秒 |
| 对局吞吐 | 约 219 决策帧/秒 |
| 平均 GPU 观察渲染 + 同步回读 | 约 0.77 毫秒/张 |
| RGB 传输总量 | 约 98.9 MiB |

GPU 耗时从初始化图像预热后开始统计，包含黑屏观察；对局耗时包含双方策略、gRPC、物理、渲染和传输，不包含构建与服务器启动。这是小场景单局测量，不代表多对局、复杂模型或高分辨率的性能保证。脚本可以重测。

### 构建说明

`Stride.AssetCompiler` 作为 build-only 依赖编译内置资源，输出目录的 `data/` 必须随程序部署。第一次构建比原来的 Raylib 版更慢。当前固定 Stride 4.4.0；其物理集成依赖 Bepu 2.5.0-beta.28，跟随 Stride 包的版本组合。

当前上游依赖还会产生 NuGet 审计警告：`SixLabors.ImageSharp 3.1.12` 和构建工具链的 `System.Security.Cryptography.Pkcs 8.0.1`。这些告警保留显示，`NU1902/NU1903` 不提升为编译错误，C# 编译警告仍按错误处理。不要把成功构建理解为依赖审计已清零。code-only 项目没有 GameSettings 资源时，资源编译器会提示使用默认设置；运行时设置在 `StrideArenaGame` 中显式配置。

## 帧与接口约定

协议文件：[`src/Arena.Protocol/Protos/arena.proto`](src/Arena.Protocol/Protos/arena.proto)。

| 接口 | 凭证 | 用途 |
| --- | --- | --- |
| `AgentGateway.Observe` | 自己的 agent token | 获取自己第一人称的 RGB24 图像，身份由 token 确定 |
| `AgentGateway.SubmitAction` | 自己的 agent token | 为某个未来帧提交一个动作 |
| `RefereeGateway.Inspect` | referee token | 获取完整观战快照，含 seed |
| `RefereeGateway.Step` | referee token | 手动模式推进 1～600 帧 |
| `RefereeGateway.Reset` | referee token | 使用指定 seed 开始新 episode |

- `frame = 0` 为初始状态。观察第 N 帧后，通常提交 `target_frame = N + 1`。
- 每个 episode 中，每个角色每帧最多接受一个动作；重复提交返回 `AlreadyExists`。
- 只接受未来 2 秒范围内的帧；过期帧、错误 episode、已结束对局返回 `FailedPrecondition`。
- 移动向量、`turn` 必须有限；移动向量长度超过 1 时归一化，`turn` 限制在 [-1, 1]。动作只作用一帧；遗漏动作时使用图像基线策略或 idle，取决于 `--no-bots`。
- `Step` 必须携带观察到的 `episode` 和 `expected_frame`，重复请求不会多推进一次。到达终局后提前停止，因此返回帧数可能少于请求值。
- `Reset` 清空动作队列并递增 episode；旧 episode 的动作不能污染新一局。重置本身不是幂等操作。
- RPC、物理与 GPU 命令在同一引擎线程串行执行，gRPC 线程通过异步命令队列等待结果；窗口和客户端拿到的是完成帧的不可变快照。
- Bepu 配置为单工作线程、确定性模式，在相同版本/机器上测试了相同 seed 与逐帧输入的重置复现；不承诺跨 CPU、显卡、驱动或运行时的物理与图像位级一致性。
- `events`、胜负、阶段等字段只存在于裁判 `Snapshot` 中；智能体接口只提供图像与调度字段。

手动模式最小交互示例（C#，引用 `Arena.Protocol` 和 `Grpc.Net.Client`）：

```csharp
using Arena.Protocol;
using Grpc.Net.Client;

using var channel = GrpcChannel.ForAddress("http://127.0.0.1:50051");
var agent = new AgentGateway.AgentGatewayClient(channel);
var observed = await agent.ObserveAsync(new ObserveRequest { Token = agentToken });
byte[] pixels = observed.Rgb24.ToByteArray();
// 将 pixels（H × W × 3，RGB）输入你的视觉策略，输出移动和转向。
await agent.SubmitActionAsync(new ActionRequest
{
    Token = agentToken,
    Episode = observed.Episode,
    TargetFrame = observed.Frame + 1,
    Move = new Vector3 { X = 0, Y = 0, Z = 1 }, // 向角色前方移动
    Turn = 0.25f // 向右转，最大速度的四分之一
});
// 由持有 referee token 的裁判，在收集双方输入后统一 Step。
```

## 示例游戏规则

- 场地 24 × 18，盒体障碍物阻挡移动和视线；角色使用半径 0.4、高度 1.2 的胶囊物理体。
- 1 名寻找者和 1 名躲藏者。默认局长 60 秒，前 3 秒寻找者不能移动或转向，收到全黑图像；第 60 帧完成时躲藏期结束。
- 寻找者速度 4.4 单位/秒，躲藏者 3.5；距离不超过 1.15 且无遮挡时捕获。超时则躲藏者获胜。
- 智能体从第一人称图像中感知场景；视场之外和遮挡后的内容不会出现。世界状态保留在服务器和裁判内部。
- 动作为相对朝向控制：`move.z > 0` 前进，`move.z < 0` 后退，`move.x > 0` 右移；`move.y` 不使用。`turn > 0` 右转，负值左转，最大 180°/秒。同一帧先转向再移动。
- `ability = "jump"`：在落地时请求跳跃，起跳速度 4 单位/秒，重力 9.81 单位/秒²。空中或躲藏期的寻找者不能起跳。未知能力字符串目前不产生效果。
- 内置与外部基线均只检查像素，通过颜色识别对手、根据画面下部墙体进行简单避障。该策略用于验证视觉交互，并不保证最优寻路或抓到对手。

## 参数与验证

```powershell
dotnet run --project src/Arena.Demo -- --help
dotnet run --project samples/Arena.Bot -- --help
dotnet test Arena.slnx --configuration Release
```

服务器常用参数：`--headless`、`--manual`、`--no-bots`、`--port 50051`、`--tick-rate 20`、`--seed 42`、`--round-seconds 60`、`--view 0|1|2`。推荐用 `--configuration Release` 运行；headless 与窗口模式均需要可工作的图形设备。

可通过 `ARENA_SEEKER_TOKEN` / `ARENA_HIDER_TOKEN` / `ARENA_REFEREE_TOKEN` 环境变量注入固定凭证；未设置时每次启动自动生成。三个凭证必须不同。示例客户端也会读取这些环境变量，`--token` 等参数优先。

图形自检命令（渲染 120 帧后截图并退出）：

```powershell
dotnet run --configuration Release --project src/Arena.Demo -- --view 2 --exit-after-frames 120 --screenshot artifacts/agent-view.png
```

`--exit-after-frames` 在图形模式计数渲染帧，在无窗口模式计数模拟帧；无窗口模式也会在终局后退出。

测试直接启动 Stride headless 图形上下文，要求可用的图形驱动；覆盖帧执行、重复/过期输入、episode 隔离、Bepu 墙体阻挡、下落、落地、跳跃、子步计数、重置复现、捕获和超时。GPU 专项验证墙后/相机背后目标不可见、黑屏期、转向变化和像素一致性，HTTP/2 集成测试同时检查身份隔离与手动等待时物理不推进。

## 原型边界

本版本是基于成熟引擎的比赛后端原型：程序化基础场景、Stride GPU 渲染、Bepu 角色物理、单局单进程。Stride 的资产、动画和编辑器能力尚未包装进比赛接口；可推动的动态道具、复杂关卡、异步流水线图像回读、多房间、回放文件和排行榜仍可在这套后端上扩展。多个并行比赛建议先采用独立进程，当前测试也避免同进程多个 Stride 主机同时运行。

默认服务仅监听回环地址，token 区分本机演示中的角色与裁判。正式举办比赛还需要在调度层补充参赛程序隔离、资源和思考时间限制、断线处理、凭证分发以及成绩 / 回放持久化。`--no-bots --manual` 已提供集成该调度层需要的观察、动作和步进基础。
