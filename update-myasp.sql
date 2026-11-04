BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102063821_AddReportsBlocksNotifications'
)
BEGIN
    CREATE TABLE [Notifications] (
        [Id] int NOT NULL IDENTITY,
        [UserId] int NOT NULL,
        [Type] nvarchar(30) NOT NULL,
        [RecipeId] int NULL,
        [Title] nvarchar(100) NOT NULL,
        [Body] nvarchar(800) NOT NULL,
        [IsRead] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Notifications] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Notifications_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102063821_AddReportsBlocksNotifications'
)
BEGIN
    CREATE TABLE [RecipeReports] (
        [Id] int NOT NULL IDENTITY,
        [RecipeId] int NOT NULL,
        [ReporterId] int NOT NULL,
        [Reason] nvarchar(20) NOT NULL,
        [Detail] nvarchar(500) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [Status] int NOT NULL,
        [Resolution] nvarchar(20) NULL,
        [ResolvedAt] datetime2 NULL,
        [ResolvedById] int NULL,
        CONSTRAINT [PK_RecipeReports] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RecipeReports_Status] CHECK ([Status] BETWEEN 0 AND 1),
        CONSTRAINT [FK_RecipeReports_Recipes_RecipeId] FOREIGN KEY ([RecipeId]) REFERENCES [Recipes] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_RecipeReports_Users_ReporterId] FOREIGN KEY ([ReporterId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_RecipeReports_Users_ResolvedById] FOREIGN KEY ([ResolvedById]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102063821_AddReportsBlocksNotifications'
)
BEGIN
    CREATE TABLE [UserBlocks] (
        [BlockerId] int NOT NULL,
        [BlockedId] int NOT NULL,
        [CreatedAt] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [PK_UserBlocks] PRIMARY KEY ([BlockerId], [BlockedId]),
        CONSTRAINT [CK_UserBlocks_NotSelf] CHECK ([BlockerId] <> [BlockedId]),
        CONSTRAINT [FK_UserBlocks_Users_BlockedId] FOREIGN KEY ([BlockedId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_UserBlocks_Users_BlockerId] FOREIGN KEY ([BlockerId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102063821_AddReportsBlocksNotifications'
)
BEGIN
    CREATE INDEX [IX_Notifications_UserId_IsRead_CreatedAt] ON [Notifications] ([UserId], [IsRead], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102063821_AddReportsBlocksNotifications'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RecipeReports_RecipeId_ReporterId] ON [RecipeReports] ([RecipeId], [ReporterId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102063821_AddReportsBlocksNotifications'
)
BEGIN
    CREATE INDEX [IX_RecipeReports_ReporterId] ON [RecipeReports] ([ReporterId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102063821_AddReportsBlocksNotifications'
)
BEGIN
    CREATE INDEX [IX_RecipeReports_ResolvedById] ON [RecipeReports] ([ResolvedById]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102063821_AddReportsBlocksNotifications'
)
BEGIN
    CREATE INDEX [IX_RecipeReports_Status_CreatedAt] ON [RecipeReports] ([Status], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102063821_AddReportsBlocksNotifications'
)
BEGIN
    CREATE INDEX [IX_UserBlocks_BlockedId] ON [UserBlocks] ([BlockedId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102063821_AddReportsBlocksNotifications'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261102063821_AddReportsBlocksNotifications', N'10.0.14');
END;

COMMIT;
GO

