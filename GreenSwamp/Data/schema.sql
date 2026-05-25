-- Этот файл только для справки. EF создаёт таблицы сам через EnsureCreated().
-- Полная схема: https://github.com/xivol/greenswamp/blob/sql/schema.sql

CREATE TABLE IF NOT EXISTS users (
    user_id     INTEGER PRIMARY KEY AUTOINCREMENT,
    username    TEXT NOT NULL UNIQUE,
    display_name TEXT NOT NULL,
    avatar_url  TEXT,
    bio         TEXT,
    created_at  DATETIME DEFAULT CURRENT_TIMESTAMP,
    is_active   INTEGER DEFAULT 1
);

CREATE TABLE IF NOT EXISTS auth (
    user_id       INTEGER PRIMARY KEY REFERENCES users(user_id),
    password_hash TEXT NOT NULL,
    last_login    DATETIME,
    reset_token   TEXT,
    token_expiry  DATETIME
);

CREATE TABLE IF NOT EXISTS posts (
    post_id        INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id        INTEGER NOT NULL REFERENCES users(user_id),
    content        TEXT NOT NULL,
    post_type      TEXT DEFAULT 'text',
    media_url      TEXT,
    media_type     TEXT,
    alt_text       TEXT,
    thumbnail_url  TEXT,
    created_at     DATETIME DEFAULT CURRENT_TIMESTAMP,
    parent_post_id INTEGER REFERENCES posts(post_id)
);

CREATE TABLE IF NOT EXISTS events (
    event_id     INTEGER PRIMARY KEY AUTOINCREMENT,
    post_id      INTEGER NOT NULL UNIQUE REFERENCES posts(post_id),
    event_time   DATETIME NOT NULL,
    location     TEXT NOT NULL,
    host_org     TEXT,
    rsvp_count   INTEGER DEFAULT 0,
    max_capacity INTEGER
);

CREATE TABLE IF NOT EXISTS interactions (
    interaction_id   INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id          INTEGER NOT NULL REFERENCES users(user_id),
    post_id          INTEGER NOT NULL REFERENCES posts(post_id),
    interaction_type TEXT NOT NULL,
    created_at       DATETIME DEFAULT CURRENT_TIMESTAMP,
    content          TEXT,
    UNIQUE(user_id, post_id, interaction_type)
);

CREATE TABLE IF NOT EXISTS tags (
    tag_id      INTEGER PRIMARY KEY AUTOINCREMENT,
    tag_name    TEXT NOT NULL UNIQUE,
    created_at  DATETIME DEFAULT CURRENT_TIMESTAMP,
    usage_count INTEGER DEFAULT 0
);

CREATE TABLE IF NOT EXISTS post_tags (
    post_id INTEGER NOT NULL REFERENCES posts(post_id),
    tag_id  INTEGER NOT NULL REFERENCES tags(tag_id),
    PRIMARY KEY (post_id, tag_id)
);
