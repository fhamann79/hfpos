import { Injectable } from '@angular/core';
import { fromEvent, Observable } from 'rxjs';
import { filter, map } from 'rxjs/operators';

@Injectable({ providedIn: 'root' })
export class PosKeyboardService {
  watch(keys: string[]): Observable<KeyboardEvent> {
    const lowered = keys.map((key) => key.toLowerCase());

    return fromEvent<KeyboardEvent>(window, 'keydown').pipe(
      filter((event) => lowered.includes(event.key.toLowerCase())),
      map((event) => {
        event.preventDefault();
        return event;
      })
    );
  }
}
