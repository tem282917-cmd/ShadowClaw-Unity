# 灯与影：ShadowClaw

Unity 第一人称猎魔游戏。玩家以圣水弩箭射击敌人的影子，利用灯火改变可瞄准的投影面积。本仓库收录评审版本的 Unity 工程。

## 获取与运行

1. 安装 Git LFS 并克隆工程。在终端运行下列命令。

   ```sh
   git lfs install
   git clone https://github.com/tem282917-cmd/ShadowClaw-Unity.git
   cd ShadowClaw-Unity
   git lfs pull
   ```

2. 首次打开工程前，在 Windows 双击根目录的 **Restore-Effects.cmd**。它从作者提供的地址获取 Gabriel Aguiar Prod. 的 Free Quick Effects Vol. 1，校验版本和文件哈希，并还原项目使用的特效。已从作者取得 BIRP v1.0 的用户也可以运行 `Tools/Restore-Effects.ps1 -PackagePath <本地 unitypackage 路径>`。[特效作者页面](https://www.patreon.com/posts/free-quick-vol-1-117366219)
3. 在 Unity Hub 添加本仓库根目录，选用 **Unity 6000.3.24f1** 打开。首次导入时请等待纹理与模型处理完成。
4. 打开 **Assets/Chapter01/Scenes/Chapter01_Main.unity**，按 **Play**。启动后，风声和英文旁白引出主菜单。选择开始狩猎后可进入山脚关卡。

## 操作

WASD 移动，鼠标瞄准，左键射击，R 装填，Shift 疾跑，空格跳跃。E 投掷圣火飞刀，获得灯心后按 Q 回血。

## Windows 构建

安装该编辑器版本的 Windows Build Support，在 **File → Build Profiles** 选择 Windows x86-64，在 Scene List 启用 `Chapter01_Main`，点击 **Build**。运行生成目录中的 `ShadowClaw.exe`。

素材署名与来源见 `ThirdPartyNotices/`、`Assets/ShadowClaw/ThirdPart