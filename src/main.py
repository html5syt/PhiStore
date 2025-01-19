import math
import time
import flet as ft
import PhiControls as phi
import random
import flet.canvas as cv

DATA = 0.0


class PhiLottery(ft.Stack):
    def __init__(self, on_click=None, n=1, page=ft.Page):
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
                        ft.Image("icon.png", ),
                        animate=ft.animation.Animation(
                            200,curve=ft.AnimationCurve.BOUNCE_OUT
                        ),
                        height=0,
                        width=100 * n,
                        alignment=ft.alignment.center,
                        margin=ft.margin.only(top=20 * n),
                    ),
                ],
                expand=True,
            ),
            # phi.PhiBack(on_click=lambda e: page.window.close(), n=n),
        ]

    def on_click(self=ft.Image, e=None, page=ft.Page, n=0.8):
        # self.controls[0].offset = ft.transform.Offset(-1.2, 0)
        # page.update()
        # time.sleep(0.1)
        self.controls[0].visible = not self.controls[0].visible
        detail=self.controls[1].controls[0]
        detail.visible = True
        # detail.height = 100 * n 
        if detail.height == 0:
            detail.height = 100 * n
        else:
            detail.offset = ft.transform.Offset(-1, 0)
            page.update()
            time.sleep(0.2)
            detail.animate = None
            page.update()
            detail.visible = False
            detail.offset = ft.transform.Offset(0, 0)
            detail.animate = ft.animation.Animation(200,curve=ft.AnimationCurve.BOUNCE_OUT)
            detail.height = 0
        page.update()


def main(page: ft.Page):
    global DATA

    # layout debug
    def on_keyboard(e: ft.KeyboardEvent):
        # print(e)
        if e.key == "S" and e.ctrl and e.shift:
            page.show_semantics_debugger = not page.show_semantics_debugger
            page.update()

    page.on_keyboard_event = on_keyboard

    # page.window.title_bar_hidden = True
    # page.window.title_bar_buttons_hidden = True
    page.bgcolor = ft.Colors.BLACK
    page.padding = 0
    page.spacing = 0
    page.fonts = {
        "Exo": "Exo-Regular.otf",
    }
    if page.platform == ft.PagePlatform.ANDROID or page.platform == ft.PagePlatform.IOS:
        # 缩放倍数
        n = 0.5
    else:
        n = 0.8

    page.theme = ft.Theme(font_family="Exo")  # 默认应用字体

    # 背景音乐
    audio1 = ft.Audio(
        src="Shop0.wav", autoplay=True, release_mode=ft.audio.ReleaseMode.LOOP
    )
    # page.overlay.append(audio1)

    # 独立组件
    datashow = phi.PhiData(n=n)
    lottery = PhiLottery(n=n, page=page)
    # 页面组件树
    page.add(
        ft.Stack(
            [
                ft.Stack(
                    # 背景层
                    [
                        ft.Image(
                            str(random.randint(1, 119)) + ".webp",
                            # src="14.png",
                            fit=ft.ImageFit.COVER,
                            expand=True,
                        ),
                        ft.Container(expand=True, blur=3, bgcolor="#A5232323"),
                    ],
                    # alignment=ft.alignment.center,
                    fit=ft.StackFit.EXPAND,
                    expand=True,
                ),
                ft.ResponsiveRow(
                    [
                        ft.Container(
                            # 返回
                            phi.PhiBack(on_click=lambda e: page.window.close(), n=n),
                            margin=ft.margin.only(top=8 * n),
                            col=1,
                        ),
                        ft.Container(
                            # 文字
                            ft.Text(
                                "Data mining",
                                color=ft.Colors.WHITE,
                                size=47 * n,
                                expand=True,
                                text_align=ft.TextAlign.CENTER,
                            ),
                            col=1,
                            margin=ft.margin.only(top=27 * n),
                        ),
                        ft.Container(
                            # data
                            datashow,
                            col=1,
                            alignment=ft.alignment.top_right,
                            margin=ft.margin.only(right=25 * n),
                            on_click=lambda e: test(e, DATA, datashow, page),
                            height=70 * n * 1.25,
                        ),
                    ],
                    columns=3,
                ),
                ft.Container(
                    # 分割线
                    ft.Divider(thickness=2, color="#EE6E6E6E"),
                    height=1,
                    alignment=ft.alignment.top_center,
                    margin=ft.margin.only(top=120 * n * 0.9),
                ),
                ft.Container(
                    lottery,
                    margin=ft.margin.only(top=190 * n),
                    alignment=ft.alignment.top_center,
                ),
                ft.Container(
                    ft.Button(
                        "test",
                        on_click=lambda e: PhiLottery.on_click(lottery, e, page, n),
                    ),
                    padding=ft.padding.all(10),
                    alignment=ft.alignment.top_center,
                    margin=ft.margin.only(top=300 * n),
                ),
            ],
            alignment=ft.alignment.center,
            fit=ft.StackFit.EXPAND,
            expand=True,
        )
    )
    page.update()


def test(e, data, datashow, page=ft.Page):
    global DATA
    DATA += 102400000
    print(data)
    for i in range(102400000):
        phi.PhiData.on_data_change(datashow, phi.hum_convert(i**5), page)
    # phi.PhiData.on_data_change(datashow, phi.hum_convert(DATA), page)


ft.app(target=main)
