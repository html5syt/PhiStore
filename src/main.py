import asyncio
import random
import flet as ft
# import flet_audio as ft_a
import PhiControls as Phi
import assets.default_json as default_json

lock = asyncio.Lock()  # 防止连续点击单抽
lock2 = False  # 防止连抽时点击单抽
lock3 = False  # 防止连续点击单抽
lock4 = False  # 防止连续点击连抽
# DATA = 104857600.0  # 初始Data


# noinspection PyTypeChecker
async def main(page: ft.Page):
    # global DATA

    # 数据读取
    # 读取抽奖列表,如无自定义则使用默认
    # TODO: 自定义抽奖列表
    lottery_list = default_json.default_json()
    # 读取Data
    try:
        Phi.storage(page=page, key="data")
    except:
        Phi.storage(page=page, key="data", value=104857600.0, mode="w")

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
    page.overlay.append(audio1)

    # 独立组件
    datashow = Phi.PhiData(n=n)
    lottery = Phi.PhiLottery(n=n, page=page)
    setting = ft.AlertDialog(
        modal=True,
        title=ft.Text("设置"),
        content=ft.Column(
            [
                ft.Text(
                    "当前Data：" + str(Phi.storage(page=page, key="data")) + " Byte"
                ),
                ft.TextField(
                    value=str(Phi.storage(page=page, key="data")), label="Data/Byte"
                ),
            ],
        ),
        actions=[
            ft.Button(
                "清空session和client_storage（危险！仅供调试）",
                on_click=lambda e: DEL(e, page),
            ),
            ft.Button("确定"),
        ],
    )

    # 事件监听
    async def lottery_on_click(e):
        global lock, lock2, lock3
        nonlocal lottery, lottery_list, page, n
        if Phi.storage(page=page, key="data") >= 1048576.0:
            if not lock2 and not lock3:  # 防止连抽时点击&连续点击单抽
                lock3 = True
                lottery.controls[0].src = "phi0101.webp"
                await Phi.PhiLottery.on_click(
                    self=lottery,
                    page=page,
                    n=n,
                    lock=lock,
                    lottery_list=lottery_list,
                    DATA=Phi.storage(page=page, key="data"),
                    datadelta=1048576,
                    datashow=datashow,
                )
                lock3 = False
                page.update()
        elif Phi.storage(page=page, key="data") < 1048576.0:
            page.snack_bar = ft.SnackBar(ft.Text("余额不足"))
            page.snack_bar.open = True
            page.update()

    async def lottery_on_click_multi(e):
        global lock, lock2, lock4
        nonlocal lottery, lottery_list
        lottery.controls[0].src = " "
        # 连抽
        if not lock4:  # 防止连续点击连抽
            lock4 = True
            if Phi.storage(page=page, key="data") >= 8388608.0:
                lock2 = True
                for i in range(1, 21):
                    await Phi.PhiLottery.on_click(
                        self=lottery,
                        page=page,
                        n=n,
                        multi=True,
                        lock=lock,
                        lottery_list=lottery_list,
                        DATA=Phi.storage(page=page, key="data"),
                        datadelta=838860.8,
                        datashow=datashow,
                    )
                lottery.controls[0].src = "phi0101.webp"
                lock2 = False
                page.update()
            elif Phi.storage(page=page, key="data") < 8388608.0:
                page.snack_bar = ft.SnackBar(ft.Text("余额不足"))
                page.snack_bar.open = True
                page.update()
            lock4 = False
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

    # noinspection PyArgumentList
    def set(e, datashow, page=ft.Page):
        nonlocal setting
        # DATA += 102400000
        # print(data)
        # for i in range(102400000):
        #     Phi.PhiData.on_data_change(datashow, Phi.hum_convert(i ** 5), page)
        # # Phi.PhiData.on_data_change(datashow, Phi.hum_convert(262144), page)
        setting.content.controls[0].value = (
            "当前Data：" + str(Phi.storage(page=page, key="data")) + " Byte"
        )
        setting.actions[1].on_click = lambda e: setting_on_submit(e, datashow, page)
        page.open(setting)

    def setting_on_submit(e, datashow, page=ft.Page):
        nonlocal setting
        Phi.storage(
            page=page,
            key="data",
            value=float(setting.content.controls[1].value),
            mode="w",
        )
        Phi.PhiData.on_data_change(
            datashow, Phi.hum_convert(Phi.storage(page=page, key="data")), page=page
        )
        page.close(setting)

    def DEL(e, page=ft.Page):
        nonlocal setting
        Phi.storage(page=page, type="DEL")
        page.close(setting)

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
                            Phi.PhiBack(on_click=lambda e: page.window.close(), n=n),
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
                            on_click=lambda e: set(e, datashow, page),
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
    # print("[Log]",Phi.storage(page=page, key="data"))
    Phi.PhiData.on_data_change(
        datashow, Phi.hum_convert(Phi.storage(page=page, key="data")), page=page
    )
    page.update()


ft.app(target=main)
