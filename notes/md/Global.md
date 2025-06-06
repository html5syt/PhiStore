# 淡入淡出

1. 实例化TransitionManager.tscn
2. 使用await $TransitionManager.transition_to("res://scenes/splash.tscn)切换场景
3. 忽略因为点击过快导致的报错，实际运行没有问题

# Git clone

1. 克隆项目到本地：`git clone --recurse-submodules --shallow-submodules <URL>`
2. 更新子模块：`git submodule update --remote --depth=1`

# 在子模块目录修复 Git 指针
`cd existing_folder`

`git rev-parse --git-dir > .git`

# push 时卡住

去除pre-push hook：`mv .git/hooks/pre-push .git/hooks/pre-push.bak`