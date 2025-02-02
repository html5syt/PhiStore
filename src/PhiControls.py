import asyncio
import random
import flet as ft
import flet.canvas as cv


def hum_convert(value, bit=2):
    """_summary_: 将字节转换为人类可读的格式

    Args:
        value (_type_): _description_

    Returns:
        _type_: _description_
    """
    # 单位：B
    units = ["B", "KB", "MB", "GB", "TB", "PB"]
    size = 1024.0
    if value < 1024.0:
        return "%.2f %s" % (value, units[1])  # 修改为返回字节单位
    for i in range(len(units)):
        if (value / size) < 1:
            return "%.*f %s" % (bit, value, units[i])  # 使用 bit 参数来指定小数位数
        value = value / size


def lottery_core(page: ft.Page,lottery_list={}):
    # TODO: 不重复抽奖
    def chance_prize(prize_list):
        """
        根据传入的奖品列表和对应的概率随机抽取一项奖品，并返回奖品名称。

        参数:
        prize_list (list of tuples): 列表，每个子列表包含奖品名称（字符串）和抽到该奖品的概率（浮点数）。
        示例：[("White", 0.7), ("Blue", 0.19), ("Purple", 0.1), ("Yellow", 0.01)]

        返回:
        str: 抽中的奖品名称。

        """
        # 从 prize_list 中提取奖品名称和对应的概率
        prizes, probabilities = zip(*prize_list)

        # 使用 random.choices 按照概率随机抽取一项奖品
        chosen_prize = random.choices(prizes, weights=probabilities, k=1)[0]

        return chosen_prize

    result = []
    rd1 = chance_prize(
        [
            ("File", 0.12),
            ("Data", 0.2),
            ("Null", 0.6),
            ("Avatar", 0.05),
            ("Illustration", 0.03),
        ]
    )
    if rd1 == "File":
        rd2 = chance_prize(
            [("White", 0.7), ("Blue", 0.19), ("Purple", 0.1), ("Yellow", 0.01)]
        )
        if rd2 == "White":
            result.append(random.choice(lottery_list["File"]["White"]))
            result.append("White")
        elif rd2 == "Blue":
            result.append(random.choice(lottery_list["File"]["Blue"]))
            result.append("Blue")
        elif rd2 == "Purple":
            result.append(random.choice(lottery_list["File"]["Purple"]))
            result.append("Purple")
        elif rd2 == "Yellow":
            result.append(random.choice(lottery_list["File"]["Yellow"]))
            result.append("Yellow")
        result.append("file.png")
        return result
    if rd1 == "Data":
        rd2 = chance_prize(
            [("White", 0.7), ("Blue", 0.15), ("Purple", 0.14), ("Yellow", 0.01)]
        )
        if rd2 == "White":
            result.append(random.choice(lottery_list["Data"]["White"]))
            result.append("White")
        elif rd2 == "Blue":
            result.append(random.choice(lottery_list["Data"]["Blue"]))
            result.append("Blue")
        elif rd2 == "Purple":
            result.append(random.choice(lottery_list["Data"]["Purple"]))
            result.append("Purple")
        elif rd2 == "Yellow":
            result.append(random.choice(lottery_list["Data"]["Yellow"]))
            result.append("Yellow")
        data = float(result[0])
        storage(
            page=page,
            key="data",
            value=float(storage(page=page, key="data", mode="r")) + data,
            mode="w",
        )
        result[0] = hum_convert(data, bit=0)
        result.append("dataicon.png")
        return result
    if rd1 == "Null":
        return ["Null", "White", "null.png"]
    if rd1 == "Avatar":
        rd2 = chance_prize([("Blue", 0.15), ("Purple", 0.14)])
        if rd2 == "Blue":
            result.append(random.choice(lottery_list["Avatar"]["Blue"]))
            result.append("Blue")
        elif rd2 == "Purple":
            result.append(random.choice(lottery_list["Avatar"]["Purple"]))
            result.append("Purple")
        result.append("avatar.png")
        return result
    if rd1 == "Illustration":
        rd2 = random.choice(lottery_list["Illustration"]["White"])
        return [rd2, "White", "illustration.png"]


def storage(
    page: ft.Page,
    key: str | None,
    value=None,
    type="c",
    mode="r",
    prefix="phistore_",
):
    """存储数据，默认使用 client_storage 存储，模式为读取，前缀为 phistore_
    Args:
        key (str): 键
        value (_type_): 值
        type (str, optional): c为持久缓存，s为session缓存，DEL为清空（仅用于调试！），默认c.
        mode (str, optional): r为读取，w为写入，d为删除,(s为查询是否存在-仅内部使用)，默认r.
        prefix (str, optional): 前缀.
    """

    async def _storage(key, mode, value=None):
        if mode == "w":
            await page.client_storage.set_async(key, value)
        elif mode == "s":
            return await page.client_storage.contains_key_async(key)

    key = prefix + key
    if type == "s":
        if mode == "r":
            if page.session.contains_key(key):
                return page.session.get(key)
            else:
                raise LookupError("Key not found in session storage")
        elif mode == "w":
            page.session.set(key, value)
        elif mode == "d":
            if page.session.contains_key(key):
                page.session.remove(key)
            else:
                raise LookupError("Key not found in session storage")
        print(f"[Log]{key}值为: {page.session.get(key)}")
    # elif type == "c":
    #     if mode == "r":
    #         if _storage(key, "s"):
    #             return page.client_storage.get(key)
    #         else:
    #             raise LookupError("Key not found in client storage")
    #     elif mode == "w":
    #         _storage(key, "w", value)
    #     elif mode == "d":
    #         if _storage(key, "s"):
    #             page.client_storage.remove(key)
    #         else:
    #             raise LookupError("Key not found in client storage")
    elif type == "DEL":
        page.client_storage.clear()
        page.session.clear()


class PhiBack(ft.Stack):
    """_summary_: 返回按钮

    Args:
        ft (_type_): _description_
    """

    def __init__(self, on_click=None, n=1):
        super().__init__()
        self.controls = [
            ft.Container(
                cv.Canvas(
                    [
                        cv.Path(
                            [
                                cv.Path.LineTo(150 * n, 0),
                                cv.Path.LineTo(124.5448267 * n, 95 * n),
                                cv.Path.LineTo(0, 95 * n),
                            ],
                            paint=ft.Paint(
                                style=ft.PaintingStyle.FILL,
                                color="#90000000",
                            ),
                        ),
                        cv.Path(
                            [
                                cv.Path.MoveTo(150 * n, 0),
                                cv.Path.LineTo(157.5 * n, 0),
                                cv.Path.LineTo(132.0448267 * n, 95 * n),
                                cv.Path.LineTo(124.5448267 * n, 95 * n),
                            ],
                            paint=ft.Paint(
                                style=ft.PaintingStyle.FILL,
                                color=ft.Colors.WHITE,
                            ),
                        ),
                    ],
                    width=157.5 * n,
                    height=95 * n,
                    expand=True,
                ),
                padding=0,
                on_click=on_click,
            ),
            ft.Container(
                ft.Image(src="back.svg"),
                margin=ft.margin.only(top=26 * n, left=44 * n),
                on_click=on_click,
                width=40 * n,
                height=40 * n,
            ),
        ]


class PhiData(ft.Stack):
    """_summary_: 数据显示

    Args:
        ft (_type_): _description_
    """

    DATA = "0.00 KB"  # 7-9位字符，可能更多，防手欠

    # print("Data: ",len(DATA))
    def __init__(self, on_click=None, n=0.7):
        super().__init__()
        n *= 1.15
        self.controls = [
            ft.Stack(
                [
                    ft.Image(src="Data.png", width=350 * n),
                    ft.ResponsiveRow(
                        [
                            ft.Image(
                                src="dataicon.png",
                                col=0.62,
                                height=70 * n,
                            ),
                            ft.Container(
                                ft.Text(
                                    spans=[
                                        ft.TextSpan(
                                            self.DATA,
                                            ft.TextStyle(
                                                size=35 * n,
                                                color=ft.Colors.WHITE,
                                                # len(data); size; margin(t); margin(r)
                                                # +1;-2.5;+2;-3
                                                # 7; 35; 12; 19
                                                # 8; 32.5; 14; 16
                                                # 9; 30; 16; 13
                                                # 10; 25; 18; 10
                                            ),
                                        ),
                                        ft.TextSpan(
                                            "  Data",
                                            ft.TextStyle(
                                                size=27 * n, color=ft.Colors.WHITE
                                            ),
                                        ),
                                    ],
                                    height=70 * n,
                                ),
                                col=1.32,
                                alignment=ft.alignment.top_right,
                                margin=ft.margin.only(top=12 * n, right=19 * n),
                                height=70 * n,
                            ),
                        ],
                        alignment=ft.alignment.top_left,
                        columns=2,
                        spacing=0,
                    ),
                ],
                width=350 * n,
            )
        ]

    def on_data_change(self, data, page=ft.Page, n=0.7):
        """_summary_: 数据改变时调用

        Args:
            data (_type_): _description_
            page (_type_, optional): _description_. Defaults to ft.Page.
            n (float, optional): _description_. Defaults to 0.7.
        """
        try:
            len(data)
        except TypeError:
            raise ValueError("Data 数值过大！")
        else:
            if (
                page.platform == ft.PagePlatform.ANDROID
                or page.platform == ft.PagePlatform.IOS
            ):
                # 缩放倍数
                n *= 0.747
            else:
                n *= 1.2
            self.DATA = data
            # print("Data: ", len(self.DATA))
            # print("Data: ", self.DATA)
            # print("DataT: ", self.controls[0].controls[1].controls[1].content.spans[0].text)
            self.controls[0].controls[1].controls[1].content.spans[0].text = self.DATA
            self.controls[0].controls[1].controls[1].margin = ft.margin.only(
                top=(12 + (2 * (len(self.DATA) - 7))) * n * 1.1,
                right=(19 - (3 * (len(self.DATA) - 7))) * n * 2,
            )
            self.controls[0].controls[1].controls[1].content.spans[0].style.size = (
                (35 - (2.5 * (len(self.DATA) - 7))) * n * 0.92
            )
            # print(self.controls[0].controls[1].controls[1].content.spans[0].style.size)
            # print(self.controls[0].controls[1].controls[1].margin)
            page.update()


class PhiLottery(ft.Stack):
    """_summary_: 抽奖中心动画

    Args:
        ft (_type_): _description_
    """

    def __init__(self, n=1, page=ft.Page):
        if (
            page.platform == ft.PagePlatform.ANDROID
            or page.platform == ft.PagePlatform.IOS
        ):
            n = n * 0.8
        super().__init__()
        textwidth = 100000
        # noinspection PyTypeChecker
        self.controls = [
            ft.Image(
                src="phi0101.webp",
                width=textwidth * n,
                height=350 * n,
                offset=ft.transform.Offset(0, 0),
                # animate_offset=ft.animation.Animation(100),
            ),
            ft.Column(
                [
                    ft.Container(
                        ft.Image(
                            "dataicon.png",
                        ),
                        height=300 * n,
                        width=textwidth * n,
                        alignment=ft.alignment.center,
                        padding=0,
                        scale=ft.transform.Scale(scale_x=0.7, scale_y=0),
                        margin=0,
                    ),
                    ft.Text(
                        "",
                        color=ft.Colors.WHITE,
                        size=30 * n,
                        text_align=ft.TextAlign.CENTER,
                        width=textwidth * n,
                        style=ft.TextStyle(height=1),
                        offset=ft.transform.Offset(0, -1.43 * n),
                    ),
                ],
                expand=True,
                alignment=ft.CrossAxisAlignment.CENTER,
                spacing=0,
                offset=ft.transform.Offset(0, 0),
                opacity=0,
                animate_offset=300,
                animate_opacity=300,
            ),
        ]

    async def on_click(
        self,
        e=None,
        page=ft.Page,
        n=0.8,
        multi=False,
        DATA=0.0,
        lock=asyncio.Lock(),
        lottery_list={},
        datadelta=0.0,
        datashow=PhiData(),
    ):
        """_summary_: 点击时调用

        Args:
            e (_type_, optional): _description_. Defaults to None.
            page (_type_, optional): _description_. Defaults to None.
            n (float, optional): _description_. Defaults to 0.8.
            multi (bool, optional): _description_. Defaults to False.
            DATA (float, optional): 传入以实现抽到Data时增加实际Data。 Defaults to 0.0.
            lock (_type_, optional): _description_. Defaults to asyncio.Lock().
            lottery_list (_type_, optional): 奖品列表. Defaults to {}.
        """
        async with (
            lock
        ):  # 确保只有一个 on_click 在执行，点几次执行几次，无忽略（写不动了
            # 注：连抽功能未实现，方法已给出
            n2 = n
            if page and (
                page.platform == ft.PagePlatform.ANDROID
                or page.platform == ft.PagePlatform.IOS
            ):
                n = n * 1.2
                n2 = n * 0.747

            detail = self.controls[1].controls[0]  # 图标
            detailText = self.controls[1].controls[1]  # 描述
            detailText.size = 30 * n
            self.controls[0].visible = not self.controls[0].visible  # ?图

            self.controls[1].animate_offset = 300
            self.controls[1].animate_opacity = 300
            page.update()
            if not self.controls[0].visible:
                self.controls[1].animate_offset = 1
                self.controls[1].animate_opacity = 1
                detail.animate = 300
                self.controls[1].opacity = 1
                self.controls[1].offset = ft.transform.Offset(0, 0)
                page.update()
                # 初始状态 -> 逐渐显示
                # DATA -= datadelta
                storage(
                    page=page,
                    key="data",
                    value=float(storage(page=page, key="data", mode="r")) - datadelta,
                    mode="w",
                )
                print("[Log]当前Data：",storage(page=page, key="data", mode="r"))
                PhiData.on_data_change(
                    datashow,
                    hum_convert(storage(page=page, key="data", mode="r")),
                    page=page,
                )
                detailText.value = ""
                # 对接抽奖函数
                result = lottery_core(page=page, lottery_list=lottery_list)
                print("[Log]抽奖结果：",result)
                text = str(result[0])
                if result[1] == "White":
                    detailText.color = ft.Colors.WHITE
                elif result[1] == "Blue":
                    detailText.color = "#00FFFF"
                elif result[1] == "Purple":
                    detailText.color = "#FF00FF"
                elif result[1] == "Yellow":
                    detailText.color = "#FCF81F"
                detail.content.src = str(result[2])

                for i in range(1, 24):
                    detail.scale = ft.transform.Scale(scale_x=0.7, scale_y=0.7 / 23 * i)
                    page.update()
                    await asyncio.sleep(0.016)
                    # scale_x和scale_y无法使用动画，只能用这种方法
                    # TODO: 文字逐渐显示(issue)
                for textTemp in text:
                    detailText.value += textTemp
                    page.update()
                    await asyncio.sleep(0.05)
                if multi:
                    await asyncio.sleep(0.25)
            else:
                # 逐渐隐藏 -> 初始状态
                self.controls[1].animate_offset = 300
                self.controls[1].animate_opacity = 300
                page.update()
                self.controls[1].opacity = 0
                offset_x = -1 * abs((page.width // 4 / (350 * n2) - 1)) * 0.05 - 0.25
                if offset_x >= -0.15:
                    offset_x -= 0.1
                if offset_x < -0.3:
                    offset_x += 0.1
                self.controls[1].offset = ft.transform.Offset(offset_x, 0)
                print("[Log]Offset：",self.controls[1].offset.x)
                page.update()
                if multi:
                    await asyncio.sleep(0.1)
                else:
                    await asyncio.sleep(0.5)
