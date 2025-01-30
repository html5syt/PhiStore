<div align=center>
<img src="https://github.com/html5syt/PhiStore/blob/master/src/assets/icon.png" >
</div>
<h1 align="center">PhiStore</h1>

尝试使用Flet还原Phigros v1.6.11的商店系统，正在开发中。部分资源（如icon）来自Phigros安装包，**禁止商业及不正当使用。**

网页版：[https://html5syt.github.io/PhiStore](https://html5syt.github.io/PhiStore)

**您可以在Action的artifact中找到最新的全平台离线安装包。**

# 另记录抽奖相关信息

8MB-10抽
1024KB-1抽

## 1. 奖品类型

| 序号 | 名称               | 类型图标                                                                                                                                                          |
|----|------------------|---------------------------------------------------------------------------------------------------------------------------------------------------------------|
| 1  | File（剧情文件）       | ![https://github.com/html5syt/PhiStore/blob/master/src/assets/file.png](https://github.com/html5syt/PhiStore/blob/master/src/assets/file.png)                 |
| 2  | Data             | ![https://github.com/html5syt/PhiStore/blob/master/src/assets/dataicon.png](https://github.com/html5syt/PhiStore/blob/master/src/assets/dataicon.png)         |
| 3  | NULL（x）          | ![https://github.com/html5syt/PhiStore/blob/master/src/assets/null.png](https://github.com/html5syt/PhiStore/blob/master/src/assets/null.png)                 |
| 4  | Avatar（头像）       | ![https://github.com/html5syt/PhiStore/blob/master/src/assets/avatar.png](https://github.com/html5syt/PhiStore/blob/master/src/assets/avatar.png)             |
| 5  | Illustration（曲绘） | ![https://github.com/html5syt/PhiStore/blob/master/src/assets/illustration.png](https://github.com/html5syt/PhiStore/blob/master/src/assets/illustration.png) |

## 2. 奖品描述颜色与爆率

| 编号  | 类型           | 类型概率/% | 颜色      | 颜色对应奖励区间【概率/%】（R为随机/未知）                                                                     |
|-----|--------------|--------|---------|---------------------------------------------------------------------------------------------|
| 1.1 | File         | 10     | 白、蓝、紫、黄 | R【70】；R【19】；R【10】；周边·判定线抱枕（一类）【1】                                                           |
| 1.2 | Data         | 40     | 白、蓝、紫、黄 | 0、256KB【70】；512KB【15】；2MB、16MB【14】；32MB、64MB、128MB【1】[注：0，128，256，512，1，2，4，8，16，32，64，128] |
| 1.3 | NULL         | 40     | 白       | 【100】                                                                                       |
| 1.4 | Avatar       | 7      | 蓝、紫     | 短名字【70】；长名字【30】                                                                             |
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
3. 同属性奖品随机抽取一件
4. 返回
    1. 描述
    2. 描述颜色
    3. 图标

## 5. Json格式

（Json：导入/出；本地最近一次自定义记录保存到本地存储，预定义列表）

```json
{
  "File": {
    "White": [
      "File,White",
      "File,White,White"
    ],
    "Blue": [
      "File,Blue",
      "File,Blue,Blue"
    ],
    "Purple": [
      "File,Purple",
      "File,Purple,Purple"
    ],
    "Yellow": [
      "File,Yellow",
      "File,Yellow,Yellow"
    ]
  },
  "Data": {
    "White": [
      262144
    ],
    "Blue": [
      524288
    ],
    "Purple": [
      2097152,
      4194304,
      8388608,
      16777216
    ],
    "Yellow": [
      33554432,
      67108864,
      134217728
    ]
  },
  "Null": {
    "White": [
      "Null"
    ]
  },
  "Avatar": {
    "Blue": [
      "Avatar,Blue",
      "Avatar,Blue,Blue"
    ],
    "Purple": [
      "Avatar,Purple",
      "Avatar,Purple,Purple"
    ]
  },
  "Illustration": {
    "White": [
      "Illustration,White",
      "Illustration,White,White"
    ]
  }
}
```
