import math
import flet as ft
import PhiControls as phi
import random
import flet.canvas as cv

DATA = 0.0


class PhiData(ft.Stack):
    datatemp = 0.0

    def __init__(self, on_click=None, n=0.7):
        self.datatemp
        super().__init__()
        n = 1.15 * n
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
                                            "29.71 MB",
                                            ft.TextStyle(
                                                size=32 * n, color=ft.Colors.WHITE
                                            ),
                                        ),
                                        ft.TextSpan(
                                            "  Data",
                                            ft.TextStyle(
                                                size=27 * n, color=ft.Colors.WHITE
                                            ),
                                        ),
                                    ],
                                ),
                                col=1.32,
                                alignment=ft.alignment.top_left,
                                margin=ft.margin.only(top=15 * n),
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
        n = 0.7

    page.theme = ft.Theme(font_family="Exo")  # 默认应用字体

    # 背景音乐
    audio1 = ft.Audio(
        src="Shop0.wav", autoplay=True, release_mode=ft.audio.ReleaseMode.LOOP
    )
    # page.overlay.append(audio1)

    page.add(
        ft.Stack(
            [
                ft.Stack(
                    # 背景层
                    [
                        ft.Image(
                            str(random.randint(1, 119)) + ".png",
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
                            # data
                            PhiData(n=n),
                            col=1,
                            alignment=ft.alignment.top_right,
                            margin=ft.margin.only(right=25 * n),
                        ),
                    ],
                    columns=2,
                ),
            ],
            alignment=ft.alignment.center,
            fit=ft.StackFit.EXPAND,
            expand=True,
        )
    )

    page.update()


ft.app(target=main)
