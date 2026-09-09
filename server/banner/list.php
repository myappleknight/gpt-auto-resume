<?php
declare(strict_types=1);

header('Content-Type: application/json; charset=utf-8');
header('Cache-Control: public, max-age=900');

$allowedExtensions = ['jpg', 'jpeg', 'png', 'webp'];
$images = [];

foreach (scandir(__DIR__) ?: [] as $entry) {
    if ($entry === '.' || $entry === '..') {
        continue;
    }

    $basename = basename($entry);
    if ($basename !== $entry) {
        continue;
    }

    $path = __DIR__ . DIRECTORY_SEPARATOR . $basename;
    if (!is_file($path)) {
        continue;
    }

    $extension = strtolower(pathinfo($basename, PATHINFO_EXTENSION));
    if (!in_array($extension, $allowedExtensions, true)) {
        continue;
    }

    $images[] = $basename;
}

natcasesort($images);

echo json_encode(
    [
        'version' => 1,
        'images' => array_values($images),
    ],
    JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE
);
