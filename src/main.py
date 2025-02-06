import asyncio
import random
import flet as ft
import datetime
import PhiControls as Phi
import default_json as default_json

lock = asyncio.Lock()  # 防止连续点击单抽
lock2 = False  # 防止连抽时点击单抽
lock3 = False  # 防止连续点击单抽
lock4 = False  # 防止连续点击连抽
# 所有print语句均为日志输出。


# noinspection PyTypeChecker
async def main(page: ft.Page):
    global lock, lock2, lock3, lock4
    
    Phi.storage(page=page, key="is_load_finish",value=False,type="s" , mode="w")  # 读取Data
    
    # 数据读取
    # 读取抽奖列表,如无自定义则使用默认
    # TODO: 自定义抽奖列表
    lottery_list = default_json.default_json()
    # 读取Data
    if not await Phi.storage(page=page, key="data", mode="s"):
        await Phi.storage(page=page, key="data", value=1073741824.0, mode="w")

    # layout debug
    def on_keyboard(e: ft.KeyboardEvent):
        if e.key == "S" and e.ctrl and e.shift:
            page.show_semantics_debugger = not page.show_semantics_debugger
            page.update()

    page.on_keyboard_event = on_keyboard

    page.bgcolor = ft.Colors.BLACK
    page.padding = 0
    page.spacing = 0
    page.fonts = {
        "Exo": "Exo-Regular.otf",
    }
    if page.platform == ft.PagePlatform.ANDROID or page.platform == ft.PagePlatform.IOS:
        # 缩放倍数
        # nt123 = 0.45
        await Phi.storage(page=page, key="n", value=0.45, mode="w")
    else:
        # nt123 = 0.8
        await Phi.storage(page=page, key="n", value=0.7, mode="w")

    page.theme = ft.Theme(font_family="Exo")  # 默认应用字体

    # 背景音乐
    try:
        import flet_audio as ft_a

        audio1 = ft_a.Audio(
            src="Shop0.wav", autoplay=True, release_mode=ft_a.audio.ReleaseMode.LOOP
        )
    except:
        print("[log-", datetime.datetime.now(), "]Audio load failed, use fallback")
        audio1 = ft.Audio(
            src="Shop0.wav", autoplay=True, release_mode=ft.audio.ReleaseMode.LOOP
        )
    # TODO: flet 0.26 win编译兼容性问题
    page.overlay.append(audio1)

    # 独立组件
    datashow = Phi.PhiData(n=await Phi.storage(page=page, key="n"))
    lottery = Phi.PhiLottery(n=await Phi.storage(page=page, key="n"), page=page)
    setting = ft.AlertDialog(
        modal=True,
        title=ft.Text("设置（当前仅供调试）"),
        content=ft.Column(
            [
                ft.Text(
                    "当前Data："
                    + str(await Phi.storage(page=page, key="data"))
                    + " Byte"
                ),
                ft.TextField(
                    value=str(await Phi.storage(page=page, key="data")),
                    label="Data/Byte",
                ),
                ft.Text("当前缩放比例：" + str(await Phi.storage(page=page, key="n"))),
                ft.TextField(
                    value=str(await Phi.storage(page=page, key="n")),
                    label="缩放倍数n（0~1）",
                ),
                ft.Text("记得看浏览器控制台！", color=ft.Colors.RED),
            ],
            scroll=True,
        ),
        actions=[
            ft.Button(
                "清空session和client_storage（危险！仅供调试）",
            ),
            ft.Button("确定"),
        ],
    )

    # 事件监听
    async def lottery_on_click(e):
        global lock, lock2, lock3
        nonlocal lottery, lottery_list, page
        if not lock2 and not lock3:  # 防止连抽时点击&连续点击单抽
            lock3 = True
            lottery.controls[0].src = "phi0101.webp"
            await Phi.PhiLottery.on_click(
                self=lottery,
                page=page,
                n=await Phi.storage(page=page, key="n"),
                lock=lock,
                lottery_list=lottery_list,
                datadelta=1048576.0,
                datashow=datashow,
            )
            lock3 = False
            page.update()

    async def lottery_on_click_multi(e):
        global lock, lock2, lock4
        nonlocal lottery, lottery_list
        lottery.controls[0].src = " "
        # 连抽
        if not lock4:  # 防止连续点击连抽
            # lock4 = True
            lock2 = True
            if await Phi.storage(page=page, key="data") >= 8388608.0:
                for i in range(1, 21):
                    await Phi.PhiLottery.on_click(
                        self=lottery,
                        page=page,
                        n=await Phi.storage(page=page, key="n"),
                        multi=True,
                        lock=lock,
                        lottery_list=lottery_list,
                        datadelta=838860.8,
                        datashow=datashow,
                    )
            elif await Phi.storage(page=page, key="data") < 8388608.0:
                print("[log-", datetime.datetime.now(), "]余额不足-连抽")
                # TODO: 前端日志输出失效，待修复
                page.snack_bar = ft.SnackBar(ft.Text("余额不足"))
                page.snack_bar.open = True
                page.update()
            lottery.controls[0].src = "phi0101.webp"
            lock2 = False
            page.update()
            # lock4 = False
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
    async def set(e):
        nonlocal setting, datashow, page
        page.open(setting)
        setting.content.controls[0].value = (
            "当前Data：" + str(await Phi.storage(page=page, key="data")) + " Byte"
        )
        setting.content.controls[1].value = str(
            await Phi.storage(page=page, key="data")
        )
        setting.content.controls[2].value = str(
            "当前缩放比例：" + str(await Phi.storage(page=page, key="n"))
        )
        setting.content.controls[3].value = str(await Phi.storage(page=page, key="n"))
        setting.actions[1].on_click = setting_on_submit
        setting.actions[0].on_click = DEL
        page.open(setting)

    async def setting_on_submit(e):
        nonlocal setting, datashow, page
        data_before = await Phi.storage(page=page, key="data")
        if float(setting.content.controls[1].value) != data_before:
            await Phi.storage(
                page=page,
                key="data",
                value=float(setting.content.controls[1].value),
                mode="w",
            )
        n_before = await Phi.storage(page=page, key="n")
        await Phi.storage(
            page=page,
            key="n",
            value=float(setting.content.controls[3].value),
            mode="w",
        )
        Phi.PhiData.on_data_change(
            datashow,
            Phi.hum_convert(await Phi.storage(page=page, key="data")),
            page=page,
            n=await Phi.storage(page=page, key="n"),
        )
        print("[WARNING]控件重构开始，可能导致页面闪烁")
        if n_before != await Phi.storage(page=page, key="n"):
            temp = page.controls
            page.controls = []
            page.update()
            page.controls = temp
            page.update()
        page.close(setting)

    async def DEL(e):
        nonlocal setting, page
        await Phi.storage(page=page, type="DEL")
        # page.close(setting)
        # 太危险了

    async def reset_data(e):
        nonlocal page
        await Phi.storage(page=page, key="data", mode="w", value=1073741824.0)
        datashow.on_data_change(
            Phi.hum_convert(await Phi.storage(page=page, key="data", mode="r")),
            page=page,
            n=await Phi.storage(page=page, key="n"),
        )
        print("[log-", datetime.datetime.now(), "]Data reset to 1073741824.0")
        # TODO: 前端日志输出失效，待修复
        page.snack_bar = ft.SnackBar(ft.Text("Data已重置至1GB"))
        page.snack_bar.open = True
        page.update()

    # 页面组件树
    page.add(
        ft.Stack(
            [
                ft.Stack(
                    # 背景层
                    [
                        ft.Image(
                            str(random.randint(1, 119)) + ".webp",
                            fit=ft.ImageFit.COVER,
                            expand=True,
                        ),
                        ft.Container(expand=True, blur=3, bgcolor="#A5232323"),
                    ],
                    fit=ft.StackFit.EXPAND,
                    expand=True,
                ),
                ft.ResponsiveRow(
                    [
                        ft.Container(
                            # 返回
                            Phi.PhiBack(
                                on_click=lambda e: page.window.close(),
                                n=await Phi.storage(page=page, key="n"),
                            ),
                            margin=ft.margin.only(
                                top=8 * await Phi.storage(page=page, key="n")
                            ),
                            col=1,
                        ),
                        ft.Container(
                            # 文字
                            ft.Text(
                                "Data mining",
                                color=ft.Colors.WHITE,
                                size=47 * await Phi.storage(page=page, key="n"),
                                expand=True,
                                text_align=ft.TextAlign.CENTER,
                            ),
                            col=1,
                            margin=ft.margin.only(
                                top=27 * await Phi.storage(page=page, key="n")
                            ),
                        ),
                        ft.Container(
                            # data
                            datashow,
                            col=1,
                            alignment=ft.alignment.top_right,
                            margin=ft.margin.only(
                                right=25 * await Phi.storage(page=page, key="n")
                            ),
                            on_long_press=set,
                            height=70 * await Phi.storage(page=page, key="n") * 1.25,
                        ),
                    ],
                    columns=3,
                ),
                ft.TransparentPointer(
                    ft.Container(
                        # 分割线
                        ft.Divider(thickness=2, color="#EE6E6E6E"),
                        height=1,
                        alignment=ft.alignment.top_center,
                        margin=ft.margin.only(
                            top=120 * await Phi.storage(page=page, key="n") * 0.9
                        ),
                    )
                ),
                ft.TransparentPointer(
                    ft.Container(
                        lottery,
                        margin=ft.margin.only(
                            top=175 * await Phi.storage(page=page, key="n")
                        ),
                        alignment=ft.alignment.top_center,
                        expand=True,
                    )
                ),
                # ft.Container(
                #     ft.Row(
                #         [
                #             ft.Button(
                #                 "test",
                #                 on_click=lottery_on_click,
                #             ),
                #             ft.Button(
                #                 "test multi",
                #                 on_click=lottery_on_click_multi,
                #             ),
                #             ft.TextButton("RESET DATA", on_click=set),
                #         ],
                #         expand=True,
                #         alignment=ft.MainAxisAlignment.CENTER,
                #     ),
                #     padding=ft.padding.all(10),
                #     alignment=ft.alignment.top_center,
                #     margin=ft.margin.only(
                #         top=550 * await Phi.storage(page=page, key="n")
                #     ),
                # ),
                ft.TransparentPointer(
                    ft.Container(
                        ft.Row(
                            [
                                ft.Container(
                                    Phi.PhiLotteryButton(
                                        n=await Phi.storage(page=page, key="n")
                                    ),
                                    on_click=lottery_on_click,
                                ),
                                ft.Container(
                                    Phi.PhiLotteryButtonM(
                                        n=await Phi.storage(page=page, key="n")
                                    ),
                                    on_click=lottery_on_click_multi,
                                ),
                            ],
                            expand=True,
                            alignment=ft.MainAxisAlignment.SPACE_EVENLY,
                        ),
                        padding=0,
                        alignment=ft.alignment.bottom_center,
                        expand=True,
                        margin=ft.margin.only(
                            bottom=220 * await Phi.storage(page=page, key="n")
                        ),
                    )
                ),
                ft.TransparentPointer(
                    ft.Container(
                        Phi.PhiStoreNav(n=await Phi.storage(page=page, key="n"),page=page),
                        alignment=ft.alignment.bottom_center,
                    )
                ),
            ],
            alignment=ft.alignment.center,
            fit=ft.StackFit.EXPAND,
            expand=True,
        )
    )
    print(
        "[log-",
        datetime.datetime.now(),
        "]缩放比例",
        await Phi.storage(page=page, key="n"),
    )
    Phi.PhiData.on_data_change(
        datashow,
        Phi.hum_convert(await Phi.storage(page=page, key="data")),
        page=page,
        n=await Phi.storage(page=page, key="n"),
    )
    page.update()
    Phi.storage(page=page, key="is_load_finish", value=True,type="s", mode="w")

ft.app(target=main)
