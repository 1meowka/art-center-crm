#nullable enable

using System.Windows;

using System.Windows.Controls;

using System.Windows.Media;

using VKR.Services;

using VKR.Views;



namespace VKR.Utilities;



public static class SectionHelp

{

    public static void Attach(Window window, AppModule module)

    {

        if (module == AppModule.Help || module == AppModule.Notifications)

            return;



        Register(window, GetTitle(module), GetBody(module));

    }



    public static void AttachTopic(Window window, string title, string body) =>

        Register(window, title, body);



    private static void Register(Window window, string title, string body)

    {

        window.Loaded += (_, _) => TryAttachToHeader(window, title, body);

    }



    public static void Show(Window owner, string title, string body)

    {

        var dialogOwner = owner.IsVisible
            ? owner
            : (Application.Current.MainWindow ?? owner);
        var dialog = new SectionHelpDialog(title, body) { Owner = dialogOwner };

        dialog.ShowDialog();

    }



    private static void TryAttachToHeader(Window window, string title, string body)

    {

        if (FindNamedElement(window, "SectionHelpFab") is Button)

            return;



        var backButton = FindBackButton(window);

        if (backButton == null)

            return;



        var helpButton = CreateHelpButton(window, title, body);



        if (backButton.Parent is StackPanel toolbar

            && toolbar.Orientation == Orientation.Horizontal)

        {

            var index = toolbar.Children.IndexOf(backButton);

            toolbar.Children.Insert(index < 0 ? toolbar.Children.Count : index, helpButton);

            return;

        }



        if (backButton.Parent is Grid grid)

        {

            var toolbarPanel = new StackPanel

            {

                Orientation = Orientation.Horizontal,

                HorizontalAlignment = HorizontalAlignment.Right,

                VerticalAlignment = VerticalAlignment.Center

            };



            grid.Children.Remove(backButton);

            toolbarPanel.Children.Add(helpButton);

            toolbarPanel.Children.Add(backButton);

            grid.Children.Add(toolbarPanel);

        }

    }



    private static Button CreateHelpButton(Window window, string title, string body)

    {

        var style = window.TryFindResource("SectionHelpFabStyle") as Style;

        var button = new Button

        {

            Name = "SectionHelpFab",

            Content = "?",

            ToolTip = "Справка по разделу",

            VerticalAlignment = VerticalAlignment.Center,

            Margin = new Thickness(0, 0, 6, 0)

        };



        if (style != null)

            button.Style = style;



        button.Click += (_, _) => Show(window, title, body);

        return button;

    }



    private static Button? FindBackButton(DependencyObject root)

    {

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)

        {

            var child = VisualTreeHelper.GetChild(root, i);



            if (child is Button button && IsBackButton(button))

                return button;



            var nested = FindBackButton(child);

            if (nested != null)

                return nested;

        }



        return null;

    }



    private static FrameworkElement? FindNamedElement(DependencyObject root, string name)

    {

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)

        {

            var child = VisualTreeHelper.GetChild(root, i);



            if (child is FrameworkElement element && element.Name == name)

                return element;



            var nested = FindNamedElement(child, name);

            if (nested != null)

                return nested;

        }



        return null;

    }



    private static bool IsBackButton(Button button)

    {

        var text = button.Content?.ToString() ?? string.Empty;

        return text.Contains("Назад", System.StringComparison.OrdinalIgnoreCase);

    }



    private static string GetTitle(AppModule module) => module switch

    {

        AppModule.Leads => "Карточки гостей",

        AppModule.TrialLessons => "Пробные занятия",

        AppModule.Students => "Клиенты",

        AppModule.Groups => "Группы и расписание",

        AppModule.Teachers => "Преподаватели",

        AppModule.Contracts => "Договоры",

        AppModule.Payments => "Оплаты",

        AppModule.Debt => "Задолженность",

        AppModule.Sales => "Продажи",

        AppModule.Expenses => "Расходы",

        AppModule.Reports => "Отчёты",

        AppModule.PlansKpi => "Планы и KPI",

        AppModule.AiAnalytics => "ИИ-аналитика",

        AppModule.Dictionaries => "Справочники",

        AppModule.MaxBot => "Чат-бот MAX",

        _ => "Справка"

    };



    public static string GetModuleBody(AppModule module) => GetBody(module);



    private static string GetBody(AppModule module) => module switch

    {

        AppModule.Leads =>

            "• Ведите входящие обращения до конвертации в обучающегося.\n" +

            "• Клиент (мама, бабушка и т.д.) — первичное контактное лицо, затем дети.\n" +

            "• Можно добавить несколько детей и несколько направлений (направления необязательны).\n" +

            "• «Конвертировать» — создаёт обучающегося и предлагает договор.\n" +

            "• «Отказ» — закрывает карточку с указанием причины.",

        AppModule.TrialLessons =>

            "• «Записать на пробное» — поиск гостя, направление, группа, преподаватель.\n" +

            "• Справа — история пробных: у кого был, обратная связь, заключён ли договор.\n" +

            "• Оплата 200 ₽ проводится как продажа «Пробное занятие».\n" +

            "• «Задачи администратора» — через 30 мин после пробного узнать, как прошло занятие.\n" +

            "• «Кандидаты на договор» — после положительного пробного, зачисление и оформление договора.\n" +

            "• Журнал внизу — недельный обзор записей на пробные.\n" +

            "• Отметить результат — итог пробного и комментарий в карточке.",

        AppModule.Students =>

            "• При входе — выбор: «Клиенты» или «Представители».\n" +

            "• Сегменты: все, действующие, ушедшие, пробные, приостановленные, архивные.\n" +

            "• Фильтр «Приход» — сезон набора (например, Осень 2026).\n" +

            "• «Привязать представителя» / «Привязать ребёнка» — связь в обе стороны.\n" +

            "• «Карточка обучающегося» — профиль, представители, договоры, группы.\n" +

            "• Для несовершеннолетних обязателен представитель.",

        AppModule.Groups =>

            "• Вкладка «Группы» — создание групп (направление + преподаватель).\n" +

            "• Вкладка «Расписание» — занятия: группа, день, время, кабинет.\n" +

            "• Нужны активная группа и кабинет из справочника.\n" +

            "• Состав группы меняется из карточки обучающегося.",

        AppModule.Teachers =>

            "• Список преподавателей, карточка, посещаемость, зарплата.\n" +

            "• «Посещаемость» — отметки по занятиям из расписания.\n" +

            "• Фильтр «только работающие» скрывает неактивных.\n" +

            "• Зарплата видна руководству, не преподавателю.",

        AppModule.Contracts =>

            "• Договор заключается на обучающегося, в том числе на несовершеннолетнего.\n" +

            "• У ребёнка родитель выбирается из его карточки: мама, папа или другой взрослый.\n" +

            "• Курс задаёт длительность (6 и 9 мес. и другие). Направления — отдельный пункт меню «Направления».\n" +

            "• Сумма рассчитывается из цены и количества занятий.\n" +

            "• «Расторгнуть» — с датой и причиной.\n" +

            "• Удаление — только администратор и директор.",

        AppModule.Payments =>

            "• Учёт поступивших оплат по договорам.\n" +

            "• «Добавить оплату» — способ и тип услуги из справочников.\n" +

            "• Фильтр по периоду и поиск по ФИО / № договора.",

        AppModule.Debt =>

            "• Дебиторская задолженность — клиенты с неоплаченным остатком по рассрочке.\n" +

            "• Срок оплаты — до 20-го числа месяца, следующего за начислением.\n" +

            "• Зелёная — до срока, синяя — отсрочка, жёлтая — 1–7 дн. просрочки, красная — более 7 дн.\n" +

            "• «Отсрочка» — фиксация переноса срока и комментария от родителя.\n" +

            "• «Выгрузить» — Excel-отчёт по дебиторке. «Заметка» — заметки для коллег.",

        AppModule.Sales =>

            "• Учёт продаж товаров, услуг и пробных занятий.\n" +

            "• Колонка «Клиент» — обучающийся или ФИО из карточки гостя (для пробных).\n" +

            "• Фильтр по году и типу (пробные / остальное), сортировка как в 1С.\n" +

            "• Сводка по выручке за выбранный период.",

        AppModule.Expenses =>

            "• Учёт расходов по категориям из справочника.\n" +

            "• Итоги по статьям за период.\n" +

            "• Удаление — администратор и директор.",

        AppModule.Reports =>

            "• Отдельная форма выгрузок в Excel (набор карточек зависит от роли).\n" +

            "• «Операционный» — планы KPI, интенсивность, продажи, пробные, ученики года, пришедшие, дебиторка, ушедшие.\n" +

            "• «Финансы» и «Учебный год» — директор, старший менеджер и администратор.\n" +

            "• «География» — школы и детсады.\n" +

            "• Экранная аналитика и ИИ — после внедрения и интеграции данных.",

        AppModule.PlansKpi =>

            "• Задайте период и нажмите «Применить».\n" +

            "• «Добавить план» — целевые KPI на период.\n" +

            "• Выберите план слева → «Рассчитать KPI» для сравнения с фактом.\n" +

            "• Карточки показывают % выполнения и цветной статус.",

        AppModule.AiAnalytics =>

            "• Экран отключён до наполнения и интеграции данных.\n" +

            "• После внедрения откроется у руководства по правам доступа.\n" +

            "• Сейчас используйте раздел «Отчёты» — выгрузка в Excel.",

        AppModule.Dictionaries =>

            "• Направления — пункт меню «Направления», кнопка «Добавить направление».\n" +

            "• Остальные справочники: кабинеты, источники, оплаты и др.\n" +

            "• Полный CRUD: добавить, изменить, удалить запись.\n" +

            "• Доступно администратору и директору.\n" +

            "• Кабинеты нужны для расписания занятий.",

        AppModule.MaxBot =>

            "• «Рассылка» — сервисные сообщения подписчикам (не реклама).\n" +

            "• «Чат с клиентами» — ответы на вопросы; Long Polling, пока открыт экран.\n" +

            "• Бот отвечает на /help, /расписание, /контакты, /направления, /start, /privacy, /stop.\n" +

            "• «Настройки бота» — токен и реквизиты (только администратор).\n" +

            "• «Проверить» тестирует токен; «Сохранить» пишет в БД.",

        _ => "Краткая справка по разделу."

    };



    public const string MaxBotBroadcastTitle = "Рассылка MAX";

    public const string MaxBotBroadcastBody =

        "1. Слева выберите папку (например, «Напоминания»).\n" +

        "2. Выберите шаблон — текст подставится справа.\n" +

        "3. Укажите аудиторию: всем (только сервисные сообщения!) или одному.\n" +

        "4. Нажмите «Отправить в MAX».\n\n" +

        "По правилам MAX запрещены массовые рекламные рассылки. Подписчики — клиенты, написавшие боту.";



    public const string MaxBotChatTitle = "Чат с клиентами MAX";

    public const string MaxBotChatBody =

        "• Слева — диалоги с клиентами, которые писали боту.\n" +

        "• Справа — переписка: выберите диалог, введите ответ и нажмите «Отправить» или Enter.\n" +

        "• Ответ уходит клиенту от имени бота в мессенджере MAX.\n" +

        "• Отвечать можно только из этой программы на компьютере центра — с телефона пока нельзя.\n" +

        "• Бот сам отвечает на команды (/start, /help и др.) — см. главный экран раздела.\n" +

        "• Long Polling работает, пока открыт этот экран (для production MAX рекомендует Webhook).\n" +

        "• Если список пуст — попросите клиента написать боту в MAX.";



    public const string NotificationStatsTitle = "Статистика уведомлений";

    public const string NotificationStatsBody =

        "• Здесь — уже выполненные задачи: опросы и продление договоров.\n" +

        "• Выберите период сверху — по дате, когда администратор отметил задачу.\n" +

        "• Вкладка «Обратная связь» — сколько нравится / не нравится и детали по каждому опросу.\n" +

        "• Вкладка «Продление» — сколько клиентов продлят, не продлят и доля продлений.\n" +

        "• Активные напоминания (что ещё нужно сделать) — в карточках на главном экране уведомлений.";



    public const string AdminTitle = "Администрирование";

    public const string AdminBody =

        "• Управление учётными записями сотрудников.\n" +

        "• Раздел доступен только руководителю (роль «Директор» / «Руководитель»).\n" +

        "• На пустой базе создаётся учётная запись руководителя admin. Пароль локальной демонстрации указан в README, после входа его нужно сменить.\n" +

        "• Администраторы и преподаватели сюда не заходят — у них свой профиль по кнопке с именем.\n" +

        "• Роли: руководитель, администратор, старший менеджер, менеджер, преподаватель.\n" +

        "• Новый пароль: минимум 8 символов, буквы и цифры. Сотрудники сами пароль не меняют.\n" +

        "• Деактивированный пользователь не сможет войти в систему.";

}

