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
        n *= 1.05
        self.DATA = data
        print("Data: ", len(self.DATA))
        print("Data: ", self.DATA)
        print("DataT: ", self.controls[0].controls[1].controls[1].content.spans[0].text)
        self.controls[0].controls[1].controls[1].content.spans[0].text = self.DATA
        self.controls[0].controls[1].controls[1].margin = ft.margin.only(
            top=(12 + (2 * (len(self.DATA) - 7))) * n * 1.1,
            right=(19 - (3 * (len(self.DATA) - 7))) * n * 2,
        )
        self.controls[0].controls[1].controls[1].content.spans[0].style.size = (
            (35 - (2.5 * (len(self.DATA) - 7))) * n * 0.96
        )
        print(self.controls[0].controls[1].controls[1].content.spans[0].style.size)
        print(self.controls[0].controls[1].controls[1].margin)
        page.update()
