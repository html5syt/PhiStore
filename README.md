<div align=center>
<img src="https://github.com/html5syt/PhiStore/blob/Flet/src/assets/icon.png" >
</div>
<h1 align="center">PhiStore</h1>

# 简介

尝试使用Godot还原Phigros v1.6.11的商店系统，V2.0版本已开发完成。部分资源（如icon）来自Phigros安装包，**禁止商业及不正当使用。**

网页版：[https://html5syt.github.io/PhiStore](https://html5syt.github.io/PhiStore)

**Release 最新版[链接→](https://github.com/html5syt/PhiStore/releases/latest)**

**2.0版本会自动同步上游Phigros更新，并自动构建包含最新定数的版本。**

# 2.0新增功能

## 1. 开屏界面

## 2. 登录系统与存档系统

登录方式：长按商店右上角的剩余Data数打开登陆界面

有3种登陆方式：

1. sessionToken登录：和使用查分bot一样，获取sessionToken后输入并点击箭头即可登录。
2. TapTap登录：
   1. 网页登录：除了web端均可用，浏览器登录完成后需手动回到app
   2. 扫码登录：使用TapTap扫码登录

登录成功后，再次长按商店右上角的剩余Data数打开存档界面，可在此处上传和下载存档。

**注意：目前程序没有设计存档冲突的处理，建议在程序中对存档进行操作之前，先进行存档下载，避免出现冲突。**

<h1 align="center">🎉完结撒花🎉</h1>

<h2 align="right">by Tim<br>Originally created by Pigeon Games<br></h2>

<h3 align="right" style="font-color:red">本项目仅供学习交流使用，禁止商业及不正当使用！<br></h3>

---



# 另记录抽奖相关信息

8MB-10抽
1024KB-1抽

## 1. 奖品类型

| 序号 | 名称               | 类型图标                                                                                                                                                          |
|----|------------------|---------------------------------------------------------------------------------------------------------------------------------------------------------------|
| 1  | File（剧情文件）       | ![https://github.com/html5syt/PhiStore/blob/Flet/src/assets/file.png](https://github.com/html5syt/PhiStore/blob/Flet/src/assets/file.png)                 |
| 2  | Data             | ![https://github.com/html5syt/PhiStore/blob/Flet/src/assets/dataicon.png](https://github.com/html5syt/PhiStore/blob/Flet/src/assets/dataicon.png)         |
| 3  | NULL（x）          | ![https://github.com/html5syt/PhiStore/blob/Flet/src/assets/null.png](https://github.com/html5syt/PhiStore/blob/Flet/src/assets/null.png)                 |
| 4  | Avatar（头像）       | ![https://github.com/html5syt/PhiStore/blob/Flet/src/assets/avatar.png](https://github.com/html5syt/PhiStore/blob/Flet/src/assets/avatar.png)             |
| 5  | Illustration（曲绘） | ![https://github.com/html5syt/PhiStore/blob/Flet/src/assets/illustration.png](https://github.com/html5syt/PhiStore/blob/Flet/src/assets/illustration.png) |

## 2. 奖品描述颜色与爆率

| 编号  | 类型           | 类型概率/% | 颜色      | 颜色对应奖励区间【概率/%】（R为随机/未知）                                                                     |
|-----|--------------|--------|---------|---------------------------------------------------------------------------------------------|
| 1.1 | File (Collection)   | 7     | 白、蓝、紫、黄 | R【70】；R【19】；R【10】；周边·判定线抱枕（一类）【1】                                                           |
| 1.2 | Data         | 20     | 白、蓝、紫、黄 | 0、256KB【70】；512KB【15】；2MB、16MB【14】；32MB、64MB、128MB【1】[注：0，128，256，512，1，2，4，8，16，32，64，128] |
| 1.3 | NULL         | 65     | 白       | 【100】                                                                                       |
| 1.4 | Avatar       | 5      | 蓝、紫     | 短名字【70】；长名字【30】                                                                             |
| 1.5 | Illustration | 3      | 白       | 【100】                                                                                       |

*注：单件奖品总爆率=类型概率\*颜色概率*

## 3. 描述颜色值

| 颜色 | 值（Hex）    |
|----|-----------|
| 白色 | `#FFFFFF` |
| 蓝色 | `#00FFFF` |
| 紫色 | `#FF00FF` |
| 黄色 | `#FCF81F` |

## 4. 大致抽奖逻辑

0. 每件奖品2个属性，分别为类型和颜色。
1. 概率抽奖：抽出类型；用抽出类型筛选出符合条件的奖品
2. 类型抽奖：抽出颜色；同上
3. 遍历奖品总表获取对应奖品
4. 同属性奖品随机抽取一件
5. 返回
    1. 描述
    2. 描述颜色
    3. 图标

