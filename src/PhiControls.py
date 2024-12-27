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
                # alignment=ft.alignment.center,
                margin=ft.margin.only(top=26*n, left=44*n),
                on_click=on_click,
                width=40*n,
                height=40*n,
            ),
        ]
        
