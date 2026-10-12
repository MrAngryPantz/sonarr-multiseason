#!/usr/bin/env python3
"""Exercise a disposable Sonarr instance using generated media and a fake client."""
import argparse
import hashlib
from email.utils import formatdate
import json
import os
from pathlib import Path
import secrets
import shutil
import socket
import sqlite3
import subprocess
import tempfile
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.request import Request, urlopen
from urllib.error import HTTPError, URLError
from urllib.parse import parse_qs, urlparse


def free_port():
    with socket.socket() as sock:
        sock.bind(('127.0.0.1', 0))
        return sock.getsockname()[1]


class Audit:
    def __init__(self, args):
        self.args = args
        self.root = Path(tempfile.mkdtemp(prefix='sonarr pack audit '))
        self.data = self.root / 'appdata'
        self.library = self.root / 'library' / 'Example Show'
        self.downloads = self.root / 'downloads'
        self.title = 'Example.Show.S01-S03.1080p.BluRay.x264-Mixed'
        self.pack = self.downloads / self.title
        self.hash = 'a' * 40
        self.key = secrets.token_hex(16)
        self.port = free_port()
        self.client_port = free_port()
        self.process = None
        self.grabs = []
        self.deletions = []
        self.rss_round = 0
        self.rss_sources = set()
        self.search_requests = []
        self.present = False
        self.state = 'downloading'
        self.log_handle = None
        for folder in (self.data, self.library, self.pack):
            folder.mkdir(parents=True)
        (self.data / 'config.xml').write_text(
            '<Config><BindAddress>127.0.0.1</BindAddress>'
            f'<Port>{self.port}</Port><ApiKey>{self.key}</ApiKey>'
            '<AuthenticationMethod>Forms</AuthenticationMethod>'
            '<AuthenticationRequired>DisabledForLocalAddresses</AuthenticationRequired>'
            '<LaunchBrowser>False</LaunchBrowser><AnalyticsEnabled>False</AnalyticsEnabled>'
            '<UpdateAutomatically>False</UpdateAutomatically><LogLevel>Debug</LogLevel></Config>')

    def api(self, path, data=None, method=None, version=5):
        request = Request(f'http://127.0.0.1:{self.port}/api/v{version}/{path}',
                          data=None if data is None else json.dumps(data).encode(),
                          headers={'X-Api-Key': self.key, 'Content-Type': 'application/json'}, method=method)
        try:
            with urlopen(request, timeout=60) as response:
                body = response.read()
                return json.loads(body) if body else None
        except HTTPError as error:
            raise RuntimeError(f'{request.get_method()} {path}: {error.code} {error.read().decode()}') from None

    def start(self):
        self.log_handle = (self.root / 'host.log').open('a')
        executable = [str(self.args.app)] if self.args.app.suffix.lower() == '.exe' else [self.args.dotnet, str(self.args.app)]
        self.process = subprocess.Popen([*executable, '-nobrowser', f'-data={self.data}'],
                                        stdout=self.log_handle, stderr=subprocess.STDOUT)
        for _ in range(180):
            if self.process.poll() is not None:
                raise RuntimeError(f'Host exited: see {self.root / "host.log"}')
            try:
                self.api('system/status')
                return
            except (URLError, RuntimeError):
                time.sleep(0.5)
        raise TimeoutError('Host did not become ready')

    def stop(self):
        if self.process is not None and self.process.poll() is None:
            self.process.terminate()
            try:
                self.process.wait(timeout=20)
            except subprocess.TimeoutExpired:
                self.process.kill()
                self.process.wait()
        if self.log_handle:
            self.log_handle.close()

    def command(self, name, **fields):
        command = self.api('command', {'name': name, **fields})
        for _ in range(180):
            result = self.api(f'command/{command["id"]}')
            if result['status'] == 'completed':
                return result
            if result['status'] == 'failed':
                raise RuntimeError(f'{name} failed: {result}')
            time.sleep(0.25)
        raise TimeoutError(name)

    def server(self):
        audit = self
        class Handler(BaseHTTPRequestHandler):
            protocol_version = 'HTTP/1.1'

            def log_message(self, *_):
                pass

            def handle(self):
                try:
                    super().handle()
                except (ConnectionResetError, BrokenPipeError):
                    # The application closes persistent connections when the trial stops it.
                    pass

            def do_GET(self):
                self.handle_request()

            def do_POST(self):
                self.handle_request()

            def handle_request(self):
                path = urlparse(self.path).path
                body = self.rfile.read(int(self.headers.get('Content-Length', '0')))
                response = {}
                if path.endswith('/api'):
                    query = parse_qs(urlparse(self.path).query)
                    if query.get('t') == ['caps']:
                        response = (
                            '<caps>'
                            '<server version="1.0"/>'
                            '<limits max="100" default="100"/>'
                            '<searching>'
                            + ('<search available="yes" supportedParams="q"/>' if '/2/' not in path else '')
                            + ('<tv-search available="yes" supportedParams="q,tvdbid,season,ep"/>' if '/1/' in path
                               else '<tv-search available="yes" supportedParams="title,season,ep"/>' if '/2/' in path else '')
                            + '</searching>'
                            '<categories>'
                            '<category id="5000" name="TV"/>'
                            '</categories>'
                            '</caps>'
                        )
                    else:
                        is_search = any(k in query for k in ('q', 'title', 'tvdbid', 'season', 'ep'))
                        if is_search:
                            audit.search_requests.append((path, query))
                        else:
                            audit.rss_sources.add(path)
                        # ID searches return episodes but miss the pack. Season/episode filters
                        # also hide it, as they do on indexers with single-season metadata.
                        restricted = any(k in query for k in ('season', 'ep', 'tvdbid'))
                        release_title = 'Example.Show.S01E01.720p.HDTV-Test' if restricted else audit.title
                        magnet = f'magnet:?xt=urn:btih:{audit.hash}'
                        guid = f'http://127.0.0.1:{audit.client_port}{path}/release/{audit.rss_round}'
                        response = (
                            f'<rss version="2.0" xmlns:torznab="http://torznab.com/schemas/2015/feed">'
                            f'<channel>'
                            f'<title>Simulated indexer</title>'
                            f'<description>Test data</description>'
                            f'<link>http://127.0.0.1</link>'
                            f'<item>'
                            f'<title>{release_title}</title>'
                            f'<guid>{guid}</guid>'
                            f'<pubDate>{formatdate(usegmt=True)}</pubDate>'
                            f'<link>{magnet}</link>'
                            f'<size>1200000000</size>'
                            f'<enclosure url="{magnet}" length="1200000000" type="application/x-bittorrent"/>'
                            f'<torznab:attr name="category" value="5000"/>'
                            f'<torznab:attr name="seeders" value="3"/>'
                            f'<torznab:attr name="peers" value="5"/>'
                            f'<torznab:attr name="infohash" value="{audit.hash}"/>'
                            f'<torznab:attr name="tvdbid" value="900000001"/>'
                            f'</item>'
                            f'</channel>'
                            f'</rss>'
                        )
                        if restricted:
                            response = response.replace(f'<guid>{guid}</guid>', f'<guid>{guid}/episode</guid>')
                elif path.endswith('/app/webapiVersion'):
                    response = '2.11.2'
                elif path.endswith('/app/version'):
                    response = 'v5.0.4'
                elif path.endswith('/auth/login'):
                    response = 'Ok.'
                elif path.endswith('/app/preferences'):
                    response = {'save_path': str(audit.downloads), 'dht': True, 'max_ratio_enabled': True,
                                'max_ratio': 1, 'max_ratio_act': 0, 'queueing_enabled': False}
                elif path.endswith('/torrents/categories'):
                    response = {'test-sonarr': {'name': 'test-sonarr', 'savePath': str(audit.downloads)}}
                elif path.endswith('/torrents/info'):
                    response = [] if not audit.present else [{
                        'hash': audit.hash, 'name': audit.title, 'size': 1200000000,
                        'progress': 0.5 if audit.state == 'downloading' else 1,
                        'state': audit.state, 'category': 'test-sonarr', 'save_path': str(audit.downloads),
                        'content_path': str(audit.pack), 'ratio': 2, 'ratio_limit': 1,
                        'seeding_time': 7200, 'eta': 0}]
                elif path.endswith('/torrents/add'):
                    audit.grabs.append(body.decode(errors='replace'))
                    audit.present = True
                    response = 'Ok.'
                elif path.endswith('/torrents/delete'):
                    audit.deletions.append(parse_qs(body.decode()))
                    audit.present = False
                encoded = response.encode() if isinstance(response, str) else json.dumps(response).encode()
                self.send_response(200)
                self.send_header('Content-Type', 'text/plain' if isinstance(response, str) else 'application/json')
                self.send_header('Content-Length', str(len(encoded)))
                self.end_headers()
                self.wfile.write(encoded)
        self.http = ThreadingHTTPServer(('127.0.0.1', self.client_port), Handler)
        threading.Thread(target=self.http.serve_forever, daemon=True).start()

    def insert(self, db, table, values):
        columns = ','.join('"' + key + '"' for key in values)
        placeholders = ','.join('?' for _ in values)
        db.execute(f'INSERT INTO "{table}" ({columns}) VALUES ({placeholders})', tuple(values.values()))

    def seed(self):
        with sqlite3.connect(self.data / 'sonarr.db') as db:
            self.insert(db, 'RootFolders', {'Path': str(self.library.parent)})
            self.insert(db, 'Series', {
                'Id': 1, 'TvdbId': 900000001, 'TvRageId': 0, 'TvMazeId': 0,
                'Title': 'Example Show', 'TitleSlug': 'example-show', 'CleanTitle': 'exampleshow',
                'SortTitle': 'example show', 'Status': 2, 'Images': '[]', 'Actors': '[]', 'Genres': '[]',
                'Ratings': '{}', 'Path': str(self.library), 'Monitored': 1, 'SeasonFolder': 1,
                'Runtime': 10, 'SeriesType': 0, 'UseSceneNumbering': 0, 'QualityProfileId': 1,
                'OriginalLanguage': 1, 'Year': 2000, 'Added': '2000-01-01 00:00:00', 'Tags': '[]',
                'Seasons': json.dumps([{'seasonNumber': s, 'monitored': True} for s in (1, 2, 3)])})
            for season in (1, 2, 3):
                for episode in (1, 2):
                    self.insert(db, 'Episodes', {
                        'Id': (season-1)*2+episode, 'SeriesId': 1, 'SeasonNumber': season,
                        'EpisodeNumber': episode, 'Title': f'Episode {episode}', 'EpisodeFileId': 0,
                        'Monitored': 1, 'AirDateUtc': '2000-01-01 00:00:00', 'AirDate': '2000-01-01',
                        'UnverifiedSceneNumbering': 0, 'Runtime': 10, 'Images': '[]', 'Ratings': '{}'})
            self.insert(db, 'DownloadClients', {
                'Id': 1, 'Enable': 1, 'Name': 'Simulated client', 'Implementation': 'QBittorrent',
                'ConfigContract': 'QBittorrentSettings', 'Tags': '[]',
                'Settings': json.dumps({'host': '127.0.0.1', 'port': self.client_port,
                                        'tvCategory': 'test-sonarr', 'recentTvPriority': 0, 'olderTvPriority': 0})})
            for indexer_id, name in enumerate(('First indexer', 'Second indexer', 'Third indexer'), 1):
                self.insert(db, 'Indexers', {
                    'Id': indexer_id, 'Name': name, 'Implementation': 'Torznab', 'ConfigContract': 'TorznabSettings',
                    'EnableRss': 1, 'EnableAutomaticSearch': 1, 'EnableInteractiveSearch': 1, 'Tags': '[]',
                    'Settings': json.dumps({'baseUrl': f'http://127.0.0.1:{self.client_port}',
                                            'apiPath': f'/indexer/{indexer_id}/api', 'categories': [5000], 'minimumSeeders': 3})})
            db.execute("UPDATE ScheduledTasks SET LastExecution = '2099-01-01 00:00:00'")

    def media(self):
        clip = self.root / 'clip.mkv'
        subprocess.run(['ffmpeg', '-hide_banner', '-loglevel', 'error', '-f', 'lavfi', '-i',
                        'color=c=black:s=1280x720:r=1', '-f', 'lavfi', '-i',
                        'anullsrc=channel_layout=stereo:sample_rate=44100', '-t', '600',
                        '-c:v', 'libx264', '-preset', 'ultrafast', '-crf', '35', '-c:a', 'aac',
                        '-b:a', '32k', str(clip)], check=True)
        self.original = self.library / 'Season 01' / 'Example.Show.S01E01.720p.HDTV.mkv'
        self.original.parent.mkdir()
        shutil.copyfile(clip, self.original)
        self.original_hash = hashlib.sha256(self.original.read_bytes()).hexdigest()
        for season in (1, 2, 3):
            for episode in (1, 2):
                if (season, episode) == (3, 2):
                    name = 'Example.Show.E02.1080p.WEB-DL.mkv'
                else:
                    quality = '720p.HDTV' if season == 1 else '1080p.WEB-DL'
                    name = f'Example.Show.S{season:02}E{episode:02}.{quality}.mkv'
                shutil.copyfile(clip, self.pack / name)

    def push(self, indexer, suffix='', seeders=10):
        return self.api('release/push', {
            'title': self.title, 'size': 1200000000, 'tvdbId': 900000001,
            'indexer': indexer, 'publishDate': '2026-01-01T00:00:00Z',
            'downloadUrl': f'http://127.0.0.1:{self.client_port}/release/{indexer.replace(" ", "-")}{suffix}',
            'magnetUrl': f'magnet:?xt=urn:btih:{self.hash}&dn=Example.Show',
            'infoHash': self.hash, 'seeders': seeders, 'leechers': 0, 'protocol': 'torrent', 'downloadClientId': 1})

    def episodes(self):
        return self.api('episode?seriesId=1')

    def process_downloads(self):
        self.command('RefreshMonitoredDownloads')
        self.command('ProcessMonitoredDownloads')

    def run(self):
        self.server()
        self.start()
        self.stop()
        self.seed()
        self.media()
        self.start()
        self.command('RescanSeries', seriesId=1)
        assert sum(e['hasFile'] for e in self.episodes()) == 1, 'Existing episode was not indexed'
        original_id = self.episodes()[0]['episodeFileId']
        profile = self.api('qualityprofile/1')
        profile.update(allowMultiSeasonPacks=True, upgradeAllowed=True)
        self.api('qualityprofile/1', profile, method='PUT')
        profile = self.api('qualityprofile/1')
        assert profile['allowMultiSeasonPacks'] and not profile['upgradeAllowed']
        assert not self.api('qualityprofile/1', version=3)['upgradeAllowed']
        print('PASS: both APIs enforce upgrades off', flush=True)
        low_seeders = self.push('First indexer', seeders=2)
        assert any(r['reason'] == 'minimumSeeders' for r in low_seeders['decision']['rejections'])
        assert not self.grabs, 'Release below the minimum seeders was grabbed'
        for query in ('seriesId=1&seasonNumber=2', 'episodeId=5'):
            results = self.api(f'release?{query}')
            packs = [r for r in results if r['release']['title'] == self.title]
            assert {r['release']['indexerId'] for r in packs} == {1, 2, 3}, 'A capability variant could not discover the pack'
            assert all(r['decision']['approved'] for r in packs), 'Discovered pack was unexpectedly rejected'
        assert any('tvdbid' in q for _, q in self.search_requests), 'ID search was not exercised'
        assert any('title' in q for _, q in self.search_requests), 'TV title search was not exercised'
        assert any(q.get('t') == ['search'] and 'q' in q for _, q in self.search_requests), 'Generic search was not exercised'
        print('PASS: episode and season searches find packs through ID, TV title and generic search capabilities', flush=True)
        self.command('RssSync' if self.args.grab_method == 'rss' else 'SeasonSearch',
                     **({} if self.args.grab_method == 'rss' else {'seriesId': 1, 'seasonNumber': 2}))
        assert len(self.grabs) == 1, 'Missing episodes did not trigger a pack grab'
        self.command('RssSync')
        assert len(self.rss_sources) == 3, f'RSS did not query all simulated indexers: {sorted(self.rss_sources)}'
        assert len(self.grabs) == 1, 'RSS duplicated the selected pack'
        self.process_downloads()
        decision = self.push('Second indexer')
        assert not decision['decision']['approved'] and len(self.grabs) == 1, 'Queued pack was grabbed twice'
        print(f'PASS: {self.args.grab_method} selects one pack across three indexers; seeders and queue deduplication work', flush=True)
        self.state = 'stoppedUP'
        self.process_downloads()
        episodes = self.episodes()
        assert sum(e['hasFile'] for e in episodes) == 5, 'Expected five episodes after partial import'
        assert not self.deletions, 'Incomplete pack was removed'
        assert self.original.exists() and hashlib.sha256(self.original.read_bytes()).hexdigest() == self.original_hash
        assert episodes[0]['episodeFileId'] == original_id
        print('PASS: mixed qualities import; existing file is unchanged; ambiguous episode retains pack', flush=True)
        queue = self.api('queue')['records'][0]
        assert [(s['seasonNumber'], s['episodeCount'], s['episodesWithFilesCount']) for s in queue['seasons']] == [(1, 2, 2), (2, 2, 2), (3, 2, 1)], 'Per-season queue counts are stale'
        print('PASS: partial-import queue counts match each season', flush=True)
        self.stop()
        with sqlite3.connect(self.data / 'sonarr.db') as db:
            self.insert(db, 'DownloadHistory', {
                'EventType': 2, 'SeriesId': 1, 'DownloadId': self.hash.upper(), 'SourceTitle': self.title,
                'Date': '2090-01-01 00:00:00', 'Protocol': 2, 'IndexerId': 0, 'DownloadClientId': 1, 'Data': '{}'})
        self.start()
        self.process_downloads()
        assert not self.deletions, 'Legacy completion history allowed removal after restart'
        assert self.api('queue')['records'], 'Retained legacy pack is hidden from queue'
        print('PASS: restart with legacy completion history retains incomplete pack in queue', flush=True)
        (self.pack / 'Example.Show.E02.1080p.WEB-DL.mkv').rename(self.pack / 'Example.Show.S03E02.1080p.WEB-DL.mkv')
        self.command('DownloadedEpisodesScan', path=str(self.pack), downloadClientId=self.hash.upper(), importMode='copy')
        assert all(e['hasFile'] for e in self.episodes()), 'Corrected episode did not import'
        self.process_downloads()
        assert len(self.deletions) == 1, 'Fully imported pack was not removed'
        assert self.original.exists() and hashlib.sha256(self.original.read_bytes()).hexdigest() == self.original_hash
        decision = self.push('Third indexer', '-another-guid')
        assert any(r['reason'] == 'diskUpgradesNotAllowed' for r in decision['decision']['rejections'])
        assert not decision['decision']['approved'] and len(self.grabs) == 1, 'Completed mixed-quality pack was grabbed again'
        self.rss_round += 1
        self.command('RssSync')
        assert len(self.grabs) == 1, 'Repeated RSS grabbed the pack again'
        print('PASS: cleanup only after all episodes exist; repeated pack is rejected across indexers', flush=True)
        print('All isolated import checks passed.', flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dotnet', default='dotnet')
    parser.add_argument('--grab-method', choices=('search', 'rss'), default='search')
    parser.add_argument('--app', required=True, type=Path)
    args = parser.parse_args()
    args.app = args.app.resolve()
    if not args.app.is_file():
        parser.error('Build the application first, then pass the path to Sonarr.dll or Sonarr.Console.exe')
    if shutil.which('ffmpeg') is None:
        parser.error('FFmpeg must be installed and on PATH')
    probe = args.app.parent / ('ffprobe.exe' if os.name == 'nt' else 'ffprobe')
    if not probe.is_file():
        parser.error('Place ffprobe beside the application before running the trial')
    audit = Audit(args)
    try:
        audit.run()
    finally:
        audit.stop()
        if hasattr(audit, 'http'):
            audit.http.shutdown()
            audit.http.server_close()
        print(f'Audit files: {audit.root}', flush=True)


if __name__ == '__main__':
    main()
