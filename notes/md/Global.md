# 淡入淡出

1. 实例化TransitionManager.tscn
2. 使用await $TransitionManager.transition_to("res://scenes/splash.tscn)切换场景
3. 忽略因为点击过快导致的报错，实际运行没有问题