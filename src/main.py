import asyncio
import flet as ft
import PhiControls as phi
import random

lock = asyncio.Lock()
DATA = 0.0


def LotteryCore(multi=False):
    pass
    


async def main(page: ft.Page):
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
    lottery = phi.PhiLottery(n=n, page=page)



    async def lottery_on_click(e):
        nonlocal lottery
        lottery.controls[0].src = "phi0101.webp"
        await phi.PhiLottery.on_click(self=lottery, page=page, n=n)
    async def lottery_on_click_multi(e):
        nonlocal lottery
        lottery.controls[0].src = " "
        # 连抽
        for i in range(1, 21):
            await phi.PhiLottery.on_click(self=lottery, page=page, n=n,multi=True)
        lottery.controls[0].src = "phi0101.webp"
        page.update()
        # nonlocal multi0
        # multi0 = [0, 1]
        # # 连抽
        # if multi0==[0,1]:
        #     multi0=[1,1]
        #     await phi.PhiLottery.on_click(self=lottery, page=page, n=n)
        #     print(multi0)
        # elif multi0==[1,1] or multi0[1]<10: #连抽中
        #     for i in range(1, 3):
        #         await phi.PhiLottery.on_click(self=lottery, page=page, n=n)
        #     multi0[1]+=1
        #     print(multi0)
        # elif multi0[1]>=10: #连抽结束
        #     multi0=[0,1]
        # 单抽
        # await phi.PhiLottery.on_click(self=lottery, page=page, n=n)

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
                    margin=ft.margin.only(top=160 * n),
                    alignment=ft.alignment.top_center,
                    expand=True,
                ),
                ft.Container(
                    ft.Button(
                        "test",
                        on_click=lottery_on_click,
                    ),
                    padding=ft.padding.all(10),
                    alignment=ft.alignment.top_center,
                    margin=ft.margin.only(top=550 * n),
                ),
                ft.Container(
                    ft.Button(
                        "test multi",
                        on_click=lottery_on_click_multi,
                    ),
                    padding=ft.padding.all(10),
                    alignment=ft.alignment.top_center,
                    margin=ft.margin.only(top=600 * n),
                ),
            ],
            alignment=ft.alignment.center,
            fit=ft.StackFit.EXPAND,
            expand=True,
        )
    )
    page.update()


def test(e, data, datashow, page=ft.Page):
    # global DATA
    # DATA += 102400000
    # print(data)
    # for i in range(102400000):
    #     phi.PhiData.on_data_change(datashow, phi.hum_convert(i**5), page)
    phi.PhiData.on_data_change(datashow, phi.hum_convert(256000), page)


ft.app(target=main)
