# 置于Phigros_Resource-master项目生成的info文件夹下，依次取消注释并运行，粘贴输出的列表到配置文件中。




# # 打开文件以读取模式
# with open('illustration.txt', 'r') as file:
#     # 读取每行文件，并将每一行作为列表的一个元素
#     lines = file.readlines()

# # 去除每行末尾的换行符
# lines = [line.strip()+" (Illustration) " for line in lines]



# print(lines)  # 打印结果列表




# import csv

# # 打开文件以读取模式
# with open('collection.tsv', 'r', newline='', encoding='gb18030') as file:
#     # 使用csv.reader读取tsv文件，指定分隔符为'\t'
#     tsv_reader = csv.reader(file, delimiter='\t')
    
#     # 初始化一个空列表来存储第二列的数据
#     second_column = []
    
#     # 遍历每一行
#     for row in tsv_reader:
#         # 检查第二列是否存在
#         if len(row) > 1:
#             # 将第二列的值添加到列表中
#             second_column.append(row[1])

# print(second_column)  # 打印结果列表



# # 打开文件以读取模式
# with open('avatar.txt', 'r', encoding='utf-8') as file:
#     # 读取每行文件，并将每一行作为列表的一个元素
#     lines = file.readlines()

# # 去除每行末尾的换行符
# lines = [line.strip() for line in lines]

# # 找到最长字符串的长度
# max_length = max(len(line) for line in lines)

# # 计算最长字符串长度除以2
# threshold = max_length // 2

# # 初始化两个空列表
# list1 = []
# list2 = []

# # 根据字符串长度将每行文字归为列表1或列表2
# for line in lines:
#     if len(line) < threshold:
#         list1.append(line+" (Avatar) ")
#     else:
#         list2.append(line+" (Avatar) ")

# print("List 1 (strings shorter than threshold):", list1)  # 打印列表1
# print("List 2 (strings longer than or equal to threshold):", list2)  # 打印列表2
