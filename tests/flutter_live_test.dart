// Run through this API repository's tests/flutter_integration.py in an isolated database.
import 'dart:convert';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:unistay_mobile/data.dart';

void main() {
  const base = String.fromEnvironment('LIVE_API_URL');
  test(
    'C# API: login, profile, photos, publish, apply, accept, cancel, logout',
    () async {
      expect(base, isNotEmpty, reason: 'Run through tests/flutter_integration.py');
      final student = ApiClient(baseUrl: base),
          owner = ApiClient(baseUrl: base);
      addTearDown(student.close);
      addTearDown(owner.close);
      final studentSession = await student.object(
        'POST',
        'auth/login/',
        body: {
          'email': 'mobile-student@example.test',
          'password': 'Local-smoke-only-382!',
        },
        authenticated: false,
      );
      final ownerSession = await owner.object(
        'POST',
        'auth/login/',
        body: {
          'email': 'mobile-owner@example.test',
          'password': 'Local-smoke-only-382!',
        },
        authenticated: false,
      );
      student.token = studentSession['token'];
      owner.token = ownerSession['token'];
      await student.object(
        'PATCH',
        'me/profile/',
        body: {
          'university': 'AITU',
          'schedule': 'early',
          'cleanliness': 'tidy',
          'smoking': false,
          'share_with_group': true,
        },
      );
      final created = await owner.object(
        'POST',
        'listings/',
        body: {
          'title': 'Mobile integration fixture',
          'description': 'Synthetic test listing',
          'city': 'Астана',
          'district': 'Есиль',
          'address': 'Test-only address',
          'university': 'AITU',
          'price': '85000.00',
          'capacity': 2,
        },
      );
      final id = created['id'];
      final upload =
          http.MultipartRequest(
              'POST',
              Uri.parse('$base/api/v1/listings/$id/photos/'),
            )
            ..headers['Authorization'] = 'Bearer ${owner.token}'
            ..files.add(
              http.MultipartFile.fromBytes(
                'image',
                base64Decode(const String.fromEnvironment('LIVE_PHOTO')),
                filename: 'fixture.png',
              ),
            );
      expect(
        (await http.Response.fromStream(
          await owner.client.send(upload),
        )).statusCode,
        201,
      );
      await owner.object(
        'PATCH',
        'listings/$id/',
        body: {'status': 'published'},
      );
      final catalog = PageData.fromJson(
        await student.object(
          'GET',
          'listings/',
          query: {'city': 'Астана', 'max_price': '90000', 'available': 'true'},
        ),
      );
      expect(catalog.items.single['id'], id);
      expect((await student.object('GET', 'listings/$id/'))['address'], null);
      final app = await student.object(
        'POST',
        'applications/',
        body: {'listing_id': id, 'message': 'Проверка Flutter клиента'},
      );
      expect(app['status'], 'pending');
      await expectLater(
        student.object('POST', 'applications/', body: {'listing_id': id}),
        throwsA(isA<ApiException>().having((e) => e.status, 'status', 409)),
      );
      await owner.object('POST', 'applications/${app['id']}/accept/');
      final accepted = await student.object('GET', 'listings/$id/');
      expect(accepted['available'], 1);
      expect(accepted['address'], 'Test-only address');
      final group = await student.request('GET', 'listings/$id/group/') as List;
      expect(group.single['name'], 'Mobile Student');
      final cancelled = await student.object(
        'POST',
        'applications/${app['id']}/cancel/',
      );
      expect(cancelled['status'], 'cancelled');
      expect((cancelled['events'] as List).length, 3);
      final freed = await student.object('GET', 'listings/$id/');
      expect(freed['available'], 2);
      expect(freed['address'], null);
      await student.request('POST', 'auth/logout/');
      await expectLater(
        student.object('GET', 'me/'),
        throwsA(isA<ApiException>().having((e) => e.status, 'status', 401)),
      );
    },
  );
}
