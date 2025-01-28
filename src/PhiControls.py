import asyncio
import random
import flet as ft
import flet.canvas as cv


def hum_convert(value):
    # 单位：B
    units = ["B","KB", "MB", "GB", "TB", "PB"]
    size = 1024.0
    if value <1024.0:
        return "0.00 KB"
    for i in range(len(units)):
        if (value / size) < 1:
            return "%.2f %s" % (value, units[i])
        value = value / size

class PhiBack(ft.Stack):
    def __init__(self, on_click=None,n=1):
        super().__init__()
        self.controls = [
            ft.Container(
                cv.Canvas(
                    [
                        cv.Path(
                            [
                                cv.Path.LineTo(150*n, 0),
                                cv.Path.LineTo(124.5448267*n, 95*n),
                                cv.Path.LineTo(0, 95*n),
                            ],
                            paint=ft.Paint(
                                style=ft.PaintingStyle.FILL,
                                color=("#90000000"),
                            ),
                        ),
                        cv.Path(
                            [
                                cv.Path.MoveTo(150*n, 0),
                                cv.Path.LineTo(157.5*n, 0),
                                cv.Path.LineTo(132.0448267*n, 95*n),
                                cv.Path.LineTo(124.5448267*n, 95*n),
                            ],
                            paint=ft.Paint(
                                style=ft.PaintingStyle.FILL,
                                color=ft.Colors.WHITE,
                            ),
                        ),
                    ],
                    width=157.5*n,
                    height=95*n,
                    expand=True,
                ),
                padding=0,
                on_click=on_click,
            ),
            ft.Container(
                ft.Image(src="back.svg"),
                margin=ft.margin.only(top=26*n, left=44*n),
                on_click=on_click,
                width=40*n,
                height=40*n,
            ),
        ]
        
class PhiData(ft.Stack):
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
        if page.platform == ft.PagePlatform.ANDROID or page.platform == ft.PagePlatform.IOS:
        # 缩放倍数
            n*=0.747
        else:
            n*=1.2
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
            (35 - (2.5 * (len(self.DATA) - 7))) * n * 0.96
        )
        # print(self.controls[0].controls[1].controls[1].content.spans[0].style.size)
        # print(self.controls[0].controls[1].controls[1].margin)
        page.update()

class PhiLottery(ft.Stack):
    def __init__(self, n=1, page=ft.Page):
        if (
            page.platform == ft.PagePlatform.ANDROID
            or page.platform == ft.PagePlatform.IOS
        ):
            n = n * 0.8
        super().__init__()
        self.controls = [
            ft.Image(
                src="phi0101.webp", 
                width=350 * n,
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
                        width=350 * n,
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
                        width=350 * n,
                        style=ft.TextStyle(height=1),
                        offset=ft.transform.Offset(0, -1.1*n),
                    ),

                ],
                expand=True,
                alignment=ft.MainAxisAlignment.START,
                spacing=0,
                offset=ft.transform.Offset(0, 0),
                opacity=0,
                animate_offset=300,
                animate_opacity=300,
            ),
        ]

    async def on_click(self, e=None, page=None, n=0.8,multi=False,DATA=0.0):
        async with (
            asyncio.Lock()
        ):  # 确保只有一个 on_click 在执行，点几次执行几次，无忽略（写不动了
            # 注：连抽功能未实现，方法已给出
            if page and (
                page.platform == ft.PagePlatform.ANDROID
                or page.platform == ft.PagePlatform.IOS
            ):
                n = n * 1.2

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
                detailText.value = ""
                # 对接抽奖函数
                text = "2 MB"
                detailpic=['','file.png','dataicon.png','null.png','avatar.png','illustration.png']
                detail.content.src = str(detailpic[random.randint(1, 5)])
                
                for i in range(1, 24):
                    detail.scale = ft.transform.Scale(scale_x=0.7, scale_y=0.7 / 23 * i)
                    page.update()
                    await asyncio.sleep(0.016)
                    # scale_x和scale_y无法使用动画，只能用这种方法
                for textTemp in text:
                    detailText.value += textTemp
                    page.update()
                    await asyncio.sleep(0.05)
            else:
                # 逐渐隐藏 -> 初始状态
                self.controls[1].animate_offset = 300
                self.controls[1].animate_opacity = 300
                page.update()
                self.controls[1].opacity = 0
                self.controls[1].offset = ft.transform.Offset(
                    -1 * (page.width // 4 / detail.width), 0
                )
                page.update()
                if multi:
                    await asyncio.sleep(0.1)
                else:
                    await asyncio.sleep(0.5)