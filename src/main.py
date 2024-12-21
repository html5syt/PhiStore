import math
import flet as ft
import PhiControls as phi
import random

class PhiData(ft.Stack):
    def __init__(self, on_click=None):
        super().__init__()
        self.controls = [
            ft.Stack(
                [
                    ft.Image(src='Data.png')
                ]
            )
        ]

def main(page: ft.Page):
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
                    [
                        ft.Image(
                            # str(random.randint(1, 119)) + ".png",
                            src="14.png",
                            fit=ft.ImageFit.COVER,
                            expand=True,
                        ),
                        ft.Container(expand=True, blur=3, bgcolor="#A5232323"),
                    ],
                    # alignment=ft.alignment.center,
                    fit=ft.StackFit.EXPAND,
                    expand=True,
                ),
                ft.Container(
                    phi.PhiBack(on_click=lambda e: page.window.close()),
                    margin=ft.margin.only(top=6),
                ),
                
            ],
            alignment=ft.alignment.center,
            fit=ft.StackFit.EXPAND,
            expand=True,
        )
    )

    page.update()


ft.app(target=main)
