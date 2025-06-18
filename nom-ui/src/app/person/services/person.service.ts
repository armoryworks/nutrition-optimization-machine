import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

@Injectable({
  providedIn: 'root',
})
export class PersonService {
  private readonly apiUrl = '/api/Person';

  constructor(private http: HttpClient) {}

  createPerson(personData: { personName: string }): Observable<{ id: number }> {
    return this.http.post<{ id: number }>(this.apiUrl, {
      identityUserId: null, // Assuming no IdentityUserId is required for now
      personName: personData.personName,
    });
  }
}
