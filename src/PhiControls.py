import flet as ft
import flet.canvas as cv

class PhiBack(ft.Stack):
    def __init__(self, on_click=None):
        super().__init__()
        self.controls = [
            ft.Container(
                cv.Canvas(
                    [
                        cv.Path(
                            [
                                cv.Path.LineTo(150, 0),
                                cv.Path.LineTo(124.5448267, 95),
                                cv.Path.LineTo(0, 95),
                            ],
                            paint=ft.Paint(
                                style=ft.PaintingStyle.FILL,
                                color=("#90000000"),
                            ),
                        ),
                        cv.Path(
                            [
                                cv.Path.MoveTo(150, 0),
                                cv.Path.LineTo(157.5, 0),
                                cv.Path.LineTo(132.0448267, 95),
                                cv.Path.LineTo(124.5448267, 95),
                            ],
                            paint=ft.Paint(
                                style=ft.PaintingStyle.FILL,
                                color=ft.colors.WHITE,
                            ),
                        ),
                    ],
                    width=157.5,
                    height=95,
                    expand=True,
                ),
                padding=0,
                on_click=on_click,
            ),
            ft.Container(
                ft.Image(src="back.svg"),
                # alignment=ft.alignment.center,
                margin=ft.margin.only(top=26, left=44),
            ),
        ]